using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    /// <summary>
    /// Owns one serialized in-memory membership replica and its authority supervisor.
    /// </summary>
    public sealed class ClusterMembershipNode
    {
        private readonly ClusterMembershipRuntime runtime;
        private readonly MembershipReplicatedLog log;
        private readonly MembershipElectionState election;
        private readonly MembershipLeaderReplication replication;
        private readonly MembershipStateMachine stateMachine;
        private readonly MembershipAppendReceiver appendReceiver;
        private readonly QuorumProofTracker proofTracker;
        private readonly TimeProvider timeProvider;
        private readonly ClusterMembershipNodeOptions options;
        private readonly SemaphoreSlim membershipChangeGate = new SemaphoreSlim(1, 1);
        private NodeReference? pendingPromotionLearner;
        private ClusterMembershipSnapshot? pendingPromotionCurrent;
        private ClusterMembershipSnapshot? pendingPromotionNext;
        private MembershipLeaderProposal? pendingPromotionProposal;
        private NodeReference? knownLeader;

        private ClusterMembershipNode(
            ClusterMembershipRuntime runtime,
            NodeReference local,
            TimeProvider timeProvider,
            ClusterMembershipNodeOptions options,
            MembershipReplicatedLog? restoredLog = null)
        {
            this.runtime = runtime;
            Local = local;
            this.timeProvider = timeProvider;
            this.options = options;
            log = restoredLog ?? new MembershipReplicatedLog();
            election = new MembershipElectionState(local, runtime, log);
            replication = new MembershipLeaderReplication(local, runtime, election, log);
            stateMachine = new MembershipStateMachine(runtime, log);
            appendReceiver = new MembershipAppendReceiver(local, runtime, election, log);
            proofTracker = new QuorumProofTracker(runtime, timeProvider, options.ProofValidity);
        }

        public IClusterMembership Membership => runtime;

        public NodeReference Local { get; }

        public bool IsLeader
        {
            get
            {
                lock (log.SyncRoot)
                {
                    return election.Role == MembershipElectionRole.Leader;
                }
            }
        }

        public static ClusterMembershipNode BootstrapNewCluster(
            NodeId node,
            NodeEndpoint clusterEndpoint,
            ClusterMembershipNodeOptions? options = null,
            TimeProvider? timeProvider = null)
        {
            if (clusterEndpoint is null)
            {
                throw new ArgumentNullException(nameof(clusterEndpoint));
            }

            var resolvedOptions = options ?? new ClusterMembershipNodeOptions();
            resolvedOptions.Validate();
            var runtime = new ClusterMembershipRuntime();
            runtime.BootstrapNewCluster(
                node,
                NodeIncarnationId.New(),
                clusterEndpoint);
            var local = runtime.Current.Members[0].Reference;
            return new ClusterMembershipNode(
                runtime,
                local,
                timeProvider ?? TimeProvider.System,
                resolvedOptions);
        }

        public static ClusterMembershipNode RestoreLearner(
            NodeReference local,
            ClusterMembershipTransfer transfer,
            ClusterMembershipNodeOptions? options = null,
            TimeProvider? timeProvider = null)
        {
            if (local is null)
            {
                throw new ArgumentNullException(nameof(local));
            }

            if (transfer is null)
            {
                throw new ArgumentNullException(nameof(transfer));
            }

            var resolvedOptions = options ?? new ClusterMembershipNodeOptions();
            resolvedOptions.Validate();
            var log = new MembershipReplicatedLog();
            var install = log.InstallSnapshot(transfer.ToSnapshot());
            if (install != MembershipSnapshotInstallStatus.Installed)
            {
                throw new InvalidOperationException(
                    $"Membership learner snapshot installation failed with status '{install}'.");
            }

            var runtime = new ClusterMembershipRuntime();
            var restored = new ClusterMembershipNode(
                runtime,
                local,
                timeProvider ?? TimeProvider.System,
                resolvedOptions,
                log);
            restored.stateMachine.ApplyCommitted();
            var snapshot = runtime.Current;
            if (snapshot.Cluster != local.Cluster
                || !snapshot.TryGetMember(local, out var member)
                || member is null
                || member.State != ClusterMemberState.Joining
                || member.IsVoter)
            {
                throw new InvalidOperationException(
                    "The installed membership snapshot does not admit the exact local learner incarnation.");
            }

            return restored;
        }

        public static async ValueTask<ClusterMembershipNode> JoinExistingClusterAsync(
            NodeId node,
            NodeEndpoint clusterEndpoint,
            IReadOnlyList<NodeEndpoint> contacts,
            IClusterMembershipTransport transport,
            ClusterMembershipNodeOptions? options = null,
            TimeProvider? timeProvider = null,
            CancellationToken cancellationToken = default)
        {
            if (clusterEndpoint is null)
            {
                throw new ArgumentNullException(nameof(clusterEndpoint));
            }

            if (contacts is null || contacts.Count == 0)
            {
                throw new ArgumentException("At least one cluster contact is required.", nameof(contacts));
            }

            if (transport is null)
            {
                throw new ArgumentNullException(nameof(transport));
            }

            var incarnation = NodeIncarnationId.New();
            var request = MembershipWireCodec.EncodeJoinRequest(node, incarnation, clusterEndpoint);
            var failures = new List<Exception>();
            for (var i = 0; i < contacts.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var responseFrame = await transport.RequestAsync(
                        contacts[i],
                        request,
                        cancellationToken).ConfigureAwait(false);
                    var response = MembershipWireCodec.DecodeJoinResponse(responseFrame);
                    if (response.Local.Node != node
                        || response.Local.Incarnation != incarnation)
                    {
                        throw new InvalidDataException(
                            "The cluster contact admitted a different node incarnation.");
                    }

                    return RestoreLearner(
                        response.Local,
                        response.Transfer,
                        options,
                        timeProvider);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            throw new AggregateException(
                "No cluster contact admitted the joining node. A failed join never bootstraps a new cluster.",
                failures);
        }

        public ClusterMembershipSnapshot CommitLocalReady()
        {
            lock (log.SyncRoot)
            {
                var current = runtime.Current;
                if (!current.TryGetMember(Local, out var member)
                    || member is null
                    || member.State != ClusterMemberState.Recovering)
                {
                    throw new InvalidOperationException(
                        "Only the recovering local incarnation can be committed as ready.");
                }

                EnsureLeadership();
                var command = MembershipCommands.SetMemberState(
                    Local,
                    ClusterMemberState.Ready);
                replication.Propose(command.Kind, command.Payload);
                if (stateMachine.ApplyCommitted() != 1)
                {
                    throw new InvalidOperationException(
                        "The local ready command did not reach the committed state machine.");
                }

                return runtime.Current;
            }
        }

        public ClusterMembershipSnapshot AdmitLearner(
            NodeId node,
            NodeIncarnationId incarnation,
            NodeEndpoint clusterEndpoint)
        {
            if (clusterEndpoint is null)
            {
                throw new ArgumentNullException(nameof(clusterEndpoint));
            }

            lock (log.SyncRoot)
            {
                var current = runtime.Current;
                for (var i = 0; i < current.Members.Count; i++)
                {
                    if (current.Members[i].Reference.Node == node)
                    {
                        if (current.Members[i].Reference.Incarnation == incarnation
                            && current.Members[i].State == ClusterMemberState.Joining
                            && !current.Members[i].IsVoter)
                        {
                            return current;
                        }

                        throw new InvalidOperationException(
                            "A committed member already uses the requested stable node id.");
                    }
                }

                EnsureLeadership();
                if (current.View.Value == long.MaxValue)
                {
                    throw new TerminalMembershipException("Membership view id is exhausted.");
                }

                var members = new List<ClusterMember>(current.Members.Count + 1);
                for (var i = 0; i < current.Members.Count; i++)
                {
                    members.Add(current.Members[i]);
                }

                members.Add(new ClusterMember(
                    new NodeReference(current.Cluster, node, incarnation),
                    ClusterMemberState.Joining,
                    clusterEndpoint,
                    isVoter: false));
                var next = new ClusterMembershipSnapshot(
                    current.Cluster,
                    new MembershipViewId(current.View.Value + 1),
                    members);
                var command = MembershipCommands.ReplaceSnapshot(next);
                replication.Propose(command.Kind, command.Payload);
                if (stateMachine.ApplyCommitted() != 1)
                {
                    throw new InvalidOperationException(
                        "The learner admission did not reach the committed state machine.");
                }

                return runtime.Current;
            }
        }

        public async ValueTask<ClusterMembershipSnapshot> AdmitLearnerAsync(
            NodeId node,
            NodeIncarnationId incarnation,
            NodeEndpoint clusterEndpoint,
            IClusterMembershipTransport transport,
            CancellationToken cancellationToken = default)
        {
            if (clusterEndpoint is null)
            {
                throw new ArgumentNullException(nameof(clusterEndpoint));
            }

            if (transport is null)
            {
                throw new ArgumentNullException(nameof(transport));
            }

            NodeReference? replacedIncarnation = null;
            lock (log.SyncRoot)
            {
                var observed = runtime.Current;
                for (var i = 0; i < observed.Members.Count; i++)
                {
                    var member = observed.Members[i];
                    if (member.Reference.Node != node)
                    {
                        continue;
                    }

                    if (member.Reference.Incarnation == incarnation
                        && member.State == ClusterMemberState.Joining
                        && !member.IsVoter)
                    {
                        return observed;
                    }

                    replacedIncarnation = member.Reference;
                    break;
                }
            }

            if (replacedIncarnation is not null)
            {
                if (replacedIncarnation == Local)
                {
                    throw new InvalidOperationException(
                        "A joining process cannot replace the active membership leader's stable node id.");
                }

                await Task.Delay(options.ProofValidity, timeProvider, cancellationToken)
                    .ConfigureAwait(false);
                await RemoveMemberAsync(replacedIncarnation, transport, cancellationToken)
                    .ConfigureAwait(false);
                return await AdmitLearnerAsync(
                    node,
                    incarnation,
                    clusterEndpoint,
                    transport,
                    cancellationToken).ConfigureAwait(false);
            }

            ClusterMembershipSnapshot current;
            MembershipLeaderProposal proposal;
            lock (log.SyncRoot)
            {
                current = runtime.Current;
                for (var i = 0; i < current.Members.Count; i++)
                {
                    var member = current.Members[i];
                    if (member.Reference.Node != node)
                    {
                        continue;
                    }

                    if (member.Reference.Incarnation == incarnation
                        && member.State == ClusterMemberState.Joining
                        && !member.IsVoter)
                    {
                        return current;
                    }

                    throw new InvalidOperationException(
                        "A committed member already uses the requested stable node id.");
                }

                EnsureLeadership();
                var members = new List<ClusterMember>(current.Members.Count + 1);
                for (var i = 0; i < current.Members.Count; i++)
                {
                    members.Add(current.Members[i]);
                }

                members.Add(new ClusterMember(
                    new NodeReference(current.Cluster, node, incarnation),
                    ClusterMemberState.Joining,
                    clusterEndpoint,
                    isVoter: false));
                var next = new ClusterMembershipSnapshot(
                    current.Cluster,
                    new MembershipViewId(current.View.Value + 1),
                    members);
                var command = MembershipCommands.ReplaceSnapshot(next);
                proposal = replication.Propose(command.Kind, command.Payload);
            }

            for (var i = 0; i < proposal.Requests.Count; i++)
            {
                var request = proposal.Requests[i];
                try
                {
                    var response = await transport.RequestAsync(
                        GetEndpoint(current, request.Target),
                        MembershipWireCodec.EncodeAppendRequest(request),
                        cancellationToken).ConfigureAwait(false);
                    replication.RecordReply(MembershipWireCodec.DecodeAppendResponse(response));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                }
            }

            lock (log.SyncRoot)
            {
                if (log.CommitIndex != log.LastIndex || stateMachine.ApplyCommitted() != 1)
                {
                    throw new InvalidOperationException(
                        "The learner admission did not reach the current voter majority.");
                }
            }

            for (var i = 0; i < proposal.Requests.Count; i++)
            {
                var initial = proposal.Requests[i];
                var commit = new MembershipAppendRequest(
                    Local,
                    initial.Target,
                    initial.Term,
                    current.View,
                    initial.Sequence,
                    new MembershipAppendBatch(
                        log.LastIndex,
                        log.LastTerm,
                        log.CommitIndex,
                        Array.Empty<MembershipLogEntry>()));
                try
                {
                    await transport.RequestAsync(
                        GetEndpoint(current, commit.Target),
                        MembershipWireCodec.EncodeAppendRequest(commit),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                }
            }

            return runtime.Current;
        }

        public async ValueTask<ClusterMembershipSnapshot> RemoveMemberAsync(
            NodeReference memberReference,
            IClusterMembershipTransport transport,
            CancellationToken cancellationToken = default)
        {
            if (memberReference is null)
            {
                throw new ArgumentNullException(nameof(memberReference));
            }

            if (transport is null)
            {
                throw new ArgumentNullException(nameof(transport));
            }

            ClusterMembershipSnapshot current;
            ClusterMembershipSnapshot next;
            MembershipLeaderProposal proposal;
            lock (log.SyncRoot)
            {
                current = runtime.Current;
                if (!current.TryGetMember(memberReference, out var existing) || existing is null)
                {
                    return current;
                }

                if (memberReference == Local)
                {
                    throw new InvalidOperationException(
                        "The current leader cannot remove itself from the membership view.");
                }

                EnsureLeadership();
                var members = current.Members
                    .Where(member => member.Reference != memberReference)
                    .ToArray();
                next = new ClusterMembershipSnapshot(
                    current.Cluster,
                    new MembershipViewId(current.View.Value + 1),
                    members);
                var command = MembershipCommands.ReplaceSnapshot(next);
                proposal = existing.IsVoter
                    ? replication.ProposeJointConfiguration(command.Kind, command.Payload, next)
                    : replication.Propose(command.Kind, command.Payload);
            }

            for (var i = 0; i < proposal.Requests.Count; i++)
            {
                var request = proposal.Requests[i];
                try
                {
                    var response = await transport.RequestAsync(
                        GetEndpoint(current, request.Target),
                        MembershipWireCodec.EncodeAppendRequest(request),
                        cancellationToken).ConfigureAwait(false);
                    replication.RecordReply(MembershipWireCodec.DecodeAppendResponse(response));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                }
            }

            lock (log.SyncRoot)
            {
                if (log.CommitIndex != log.LastIndex)
                {
                    throw new InvalidOperationException(
                        "The member removal did not reach the required voter majorities.");
                }

                if (runtime.Current.View != next.View && stateMachine.ApplyCommitted() != 1)
                {
                    throw new InvalidOperationException(
                        "The committed member removal was not applied locally.");
                }
            }

            for (var i = 0; i < proposal.Requests.Count; i++)
            {
                var initial = proposal.Requests[i];
                var commit = new MembershipAppendRequest(
                    Local,
                    initial.Target,
                    initial.Term,
                    current.View,
                    initial.Sequence,
                    new MembershipAppendBatch(
                        log.LastIndex,
                        log.LastTerm,
                        log.CommitIndex,
                        Array.Empty<MembershipLogEntry>()));
                try
                {
                    await transport.RequestAsync(
                        GetEndpoint(current, commit.Target),
                        MembershipWireCodec.EncodeAppendRequest(commit),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                }
            }

            return runtime.Current;
        }

        public ClusterMembershipTransfer CreateCatchUpTransfer()
        {
            lock (log.SyncRoot)
            {
                if (log.CommitIndex <= 0 || log.CommitIndex != log.LastIndex)
                {
                    throw new InvalidOperationException(
                        "A catch-up transfer requires a non-empty fully committed membership log.");
                }

                return new ClusterMembershipTransfer(MembershipSnapshotCodec.Create(
                    log.CommitIndex,
                    log.LastTerm,
                    runtime.Current));
            }
        }

        public async ValueTask<ClusterMembershipSnapshot> PromoteLearnerAsync(
            NodeReference learner,
            IClusterMembershipTransport transport,
            CancellationToken cancellationToken = default)
        {
            if (learner is null)
            {
                throw new ArgumentNullException(nameof(learner));
            }

            if (transport is null)
            {
                throw new ArgumentNullException(nameof(transport));
            }

            await membershipChangeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await PromoteLearnerCoreAsync(learner, transport, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                membershipChangeGate.Release();
            }
        }

        private async ValueTask<ClusterMembershipSnapshot> PromoteLearnerCoreAsync(
            NodeReference learner,
            IClusterMembershipTransport transport,
            CancellationToken cancellationToken)
        {
            await CatchUpLearnerAsync(learner, transport, cancellationToken)
                .ConfigureAwait(false);

            MembershipLeaderProposal proposal;
            ClusterMembershipSnapshot current;
            ClusterMembershipSnapshot next;
            lock (log.SyncRoot)
            {
                current = runtime.Current;
                if (!current.TryGetMember(learner, out var existing)
                    || existing is null)
                {
                    throw new InvalidOperationException(
                        "Only an exact committed learner can be promoted.");
                }

                EnsureLeadership();
                if (pendingPromotionLearner == learner
                    && pendingPromotionCurrent is not null
                    && pendingPromotionNext is not null
                    && pendingPromotionProposal is not null)
                {
                    current = pendingPromotionCurrent;
                    next = pendingPromotionNext;
                    proposal = pendingPromotionProposal;
                }
                else
                {
                    if (existing.IsVoter && existing.State == ClusterMemberState.Recovering)
                    {
                        return current;
                    }

                    if (existing.State != ClusterMemberState.Joining || existing.IsVoter)
                    {
                        throw new InvalidOperationException(
                            "Only a committed non-voting learner can be promoted.");
                    }

                    var members = new List<ClusterMember>(current.Members.Count);
                    for (var i = 0; i < current.Members.Count; i++)
                    {
                        var member = current.Members[i];
                        members.Add(member.Reference == learner
                            ? new ClusterMember(
                                member.Reference,
                                ClusterMemberState.Recovering,
                                member.ClusterEndpoint,
                                isVoter: true,
                                member.Labels,
                                member.Advertisements,
                                member.ActorHosts,
                                member.StartupActors)
                            : member);
                    }

                    next = new ClusterMembershipSnapshot(
                        current.Cluster,
                        new MembershipViewId(current.View.Value + 1),
                        members);
                    var command = MembershipCommands.ReplaceSnapshot(next);
                    proposal = replication.ProposeJointConfiguration(
                        command.Kind,
                        command.Payload,
                        next);
                    pendingPromotionLearner = learner;
                    pendingPromotionCurrent = current;
                    pendingPromotionNext = next;
                    pendingPromotionProposal = proposal;
                }
            }

            for (var i = 0; i < proposal.Requests.Count; i++)
            {
                var request = proposal.Requests[i];
                try
                {
                    var endpoint = GetEndpoint(current, request.Target);
                    var replyFrame = await transport.RequestAsync(
                        endpoint,
                        MembershipWireCodec.EncodeAppendRequest(request),
                        cancellationToken).ConfigureAwait(false);
                    replication.RecordReply(MembershipWireCodec.DecodeAppendResponse(replyFrame));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                }
            }

            lock (log.SyncRoot)
            {
                if (log.CommitIndex != log.LastIndex)
                {
                    throw new InvalidOperationException(
                        "The joint learner promotion did not reach both voter majorities.");
                }

                if (runtime.Current.View != next.View && stateMachine.ApplyCommitted() != 1)
                {
                    throw new InvalidOperationException(
                        "The committed joint learner promotion was not applied locally.");
                }
            }

            for (var i = 0; i < proposal.Requests.Count; i++)
            {
                var initial = proposal.Requests[i];
                var commit = new MembershipAppendRequest(
                    Local,
                    initial.Target,
                    initial.Term,
                    current.View,
                    initial.Sequence,
                    new MembershipAppendBatch(
                        log.LastIndex,
                        log.LastTerm,
                        log.CommitIndex,
                        Array.Empty<MembershipLogEntry>()));
                try
                {
                    var endpoint = GetEndpoint(current, commit.Target);
                    var commitReply = await transport.RequestAsync(
                        endpoint,
                        MembershipWireCodec.EncodeAppendRequest(commit),
                        cancellationToken).ConfigureAwait(false);
                    if (!MembershipWireCodec.DecodeAppendResponse(commitReply).Accepted)
                    {
                        throw new InvalidOperationException(
                            "A promoted membership replica did not accept the committed joint view.");
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                }
            }

            lock (log.SyncRoot)
            {
                pendingPromotionLearner = null;
                pendingPromotionCurrent = null;
                pendingPromotionNext = null;
                pendingPromotionProposal = null;
            }

            return runtime.Current;
        }

        private async ValueTask CatchUpLearnerAsync(
            NodeReference learner,
            IClusterMembershipTransport transport,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                MembershipAppendRequest? request;
                lock (log.SyncRoot)
                {
                    request = replication.CreateLearnerCatchUpRequest(learner);
                }

                if (request is null)
                {
                    return;
                }

                var response = await transport.RequestAsync(
                    GetEndpoint(runtime.Current, learner),
                    MembershipWireCodec.EncodeAppendRequest(request),
                    cancellationToken).ConfigureAwait(false);
                var reply = MembershipWireCodec.DecodeAppendResponse(response);
                if (!replication.RecordLearnerCatchUpReply(learner, reply))
                {
                    throw new InvalidOperationException(
                        "The learner did not accept its committed membership catch-up batch.");
                }
            }
        }

        public async ValueTask<ClusterMembershipSnapshot> CommitMemberReadyAsync(
            NodeReference target,
            IClusterMembershipTransport transport,
            CancellationToken cancellationToken = default)
        {
            if (!runtime.Current.TryGetMember(target, out var member) || member is null)
            {
                throw new InvalidOperationException("The ready target is not a current member.");
            }

            return await CommitMemberReadyDescriptorAsync(
                member,
                transport,
                cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<ClusterMembershipSnapshot> CommitMemberReadyDescriptorAsync(
            ClusterMember readyDescriptor,
            IClusterMembershipTransport transport,
            CancellationToken cancellationToken = default)
        {
            await membershipChangeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await CommitMemberReadyDescriptorCoreAsync(
                    readyDescriptor,
                    transport,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                membershipChangeGate.Release();
            }
        }

        private async ValueTask<ClusterMembershipSnapshot> CommitMemberReadyDescriptorCoreAsync(
            ClusterMember readyDescriptor,
            IClusterMembershipTransport transport,
            CancellationToken cancellationToken)
        {
            if (readyDescriptor is null)
            {
                throw new ArgumentNullException(nameof(readyDescriptor));
            }

            if (transport is null)
            {
                throw new ArgumentNullException(nameof(transport));
            }

            ClusterMembershipSnapshot current;
            MembershipLeaderProposal proposal;
            lock (log.SyncRoot)
            {
                current = runtime.Current;
                var target = readyDescriptor.Reference;
                if (!current.TryGetMember(target, out var member) || member is null)
                {
                    throw new InvalidOperationException("The ready target is not a current member.");
                }

                if (member.State is not (ClusterMemberState.Recovering or ClusterMemberState.Ready)
                    || !member.IsVoter)
                {
                    throw new InvalidOperationException(
                        "Only a recovering or ready committed voter can publish a ready descriptor.");
                }

                EnsureLeadership();
                var members = new List<ClusterMember>(current.Members.Count);
                for (var i = 0; i < current.Members.Count; i++)
                {
                    var currentMember = current.Members[i];
                    members.Add(currentMember.Reference == target
                        ? new ClusterMember(
                            target,
                            ClusterMemberState.Ready,
                            readyDescriptor.ClusterEndpoint,
                            isVoter: true,
                            readyDescriptor.Labels,
                            readyDescriptor.Advertisements,
                            readyDescriptor.ActorHosts,
                            readyDescriptor.StartupActors)
                        : currentMember);
                }

                var next = new ClusterMembershipSnapshot(
                    current.Cluster,
                    new MembershipViewId(current.View.Value + 1),
                    members);
                var command = MembershipCommands.ReplaceSnapshot(next);
                proposal = replication.Propose(command.Kind, command.Payload);
            }

            for (var i = 0; i < proposal.Requests.Count; i++)
            {
                var request = proposal.Requests[i];
                try
                {
                    var response = await transport.RequestAsync(
                        GetEndpoint(current, request.Target),
                        MembershipWireCodec.EncodeAppendRequest(request),
                        cancellationToken).ConfigureAwait(false);
                    replication.RecordReply(MembershipWireCodec.DecodeAppendResponse(response));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                }
            }

            lock (log.SyncRoot)
            {
                if (log.CommitIndex != log.LastIndex || stateMachine.ApplyCommitted() != 1)
                {
                    throw new InvalidOperationException(
                        "The member-ready command did not reach a voter majority.");
                }
            }

            for (var i = 0; i < proposal.Requests.Count; i++)
            {
                var initial = proposal.Requests[i];
                var commit = new MembershipAppendRequest(
                    Local,
                    initial.Target,
                    initial.Term,
                    current.View,
                    initial.Sequence,
                    new MembershipAppendBatch(
                        log.LastIndex,
                        log.LastTerm,
                        log.CommitIndex,
                        Array.Empty<MembershipLogEntry>()));
                try
                {
                    var response = await transport.RequestAsync(
                        GetEndpoint(current, commit.Target),
                        MembershipWireCodec.EncodeAppendRequest(commit),
                        cancellationToken).ConfigureAwait(false);
                    if (!MembershipWireCodec.DecodeAppendResponse(response).Accepted)
                    {
                        throw new InvalidOperationException(
                            "A membership replica rejected the committed member-ready view.");
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                }
            }

            return runtime.Current;
        }

        internal ValueTask<ClusterMembershipTransportFrame> HandleTransportRequestAsync(
            ClusterMembershipTransportFrame request,
            CancellationToken cancellationToken = default)
        {
            return HandleTransportRequestAsync(request, null, cancellationToken);
        }

        public async ValueTask<ClusterMembershipTransportFrame> HandleTransportRequestAsync(
            ClusterMembershipTransportFrame request,
            IClusterMembershipTransport? transport,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (MembershipWireCodec.IsJoinRequest(request))
            {
                if (!IsLeader
                    && transport is not null
                    && TryGetKnownLeaderEndpoint(out var leaderEndpoint))
                {
                    return await transport.RequestAsync(
                        leaderEndpoint!,
                        request,
                        cancellationToken).ConfigureAwait(false);
                }

                var join = MembershipWireCodec.DecodeJoinRequest(request);
                var committed = transport is null
                    ? AdmitLearner(join.Node, join.Incarnation, join.Endpoint)
                    : await AdmitLearnerAsync(
                        join.Node,
                        join.Incarnation,
                        join.Endpoint,
                        transport,
                        cancellationToken).ConfigureAwait(false);
                var admitted = committed.Members[committed.Members.Count - 1].Reference;
                if (admitted.Node != join.Node || admitted.Incarnation != join.Incarnation)
                {
                    for (var i = 0; i < committed.Members.Count; i++)
                    {
                        if (committed.Members[i].Reference.Node == join.Node
                            && committed.Members[i].Reference.Incarnation == join.Incarnation)
                        {
                            admitted = committed.Members[i].Reference;
                            break;
                        }
                    }
                }

                var transfer = CreateCatchUpTransfer();
                var transferred = MembershipSnapshotCodec.Decode(transfer.Payload.Span);
                replication.RecordLearnerTransfer(admitted, transferred.View);
                return MembershipWireCodec.EncodeJoinResponse(admitted, transfer);
            }

            if (MembershipWireCodec.IsAppendRequest(request))
            {
                var append = MembershipWireCodec.DecodeAppendRequest(request);
                var result = appendReceiver.Append(append);
                if (result.Status == MembershipAppendReceiveStatus.Accepted)
                {
                    lock (log.SyncRoot)
                    {
                        knownLeader = append.Source;
                    }
                    stateMachine.ApplyCommitted();
                }

                return MembershipWireCodec.EncodeAppendResponse(
                    append,
                    result,
                    runtime.Current.View);
            }

            if (MembershipWireCodec.IsVoteRequest(request))
            {
                var vote = MembershipWireCodec.DecodeVoteRequest(request);
                return MembershipWireCodec.EncodeVoteResponse(
                    vote,
                    election.RequestVote(vote));
            }

            if (MembershipWireCodec.IsProof(request))
            {
                return MembershipWireCodec.EncodeProofResponse(
                    proofTracker.TryAccept(MembershipWireCodec.DecodeProof(request)));
            }

            if (MembershipWireCodec.IsPromoteRequest(request))
            {
                if (transport is null)
                {
                    throw new InvalidOperationException(
                        "Learner promotion requires a membership transport to reach every voter.");
                }

                if (!IsLeader && TryGetKnownLeaderEndpoint(out var leaderEndpoint))
                {
                    return await transport.RequestAsync(
                        leaderEndpoint!,
                        request,
                        cancellationToken).ConfigureAwait(false);
                }

                var promotion = MembershipWireCodec.DecodePromoteRequest(request);
                replication.RecordLearnerProgress(
                    promotion.Learner,
                    promotion.View,
                    promotion.MatchIndex);
                var promoted = await PromoteLearnerAsync(
                    promotion.Learner,
                    transport,
                    cancellationToken).ConfigureAwait(false);
                return MembershipWireCodec.EncodePromoteResponse(promoted);
            }

            if (MembershipWireCodec.IsReadyRequest(request))
            {
                if (transport is null)
                {
                    throw new InvalidOperationException(
                        "Member-ready commit requires a membership transport to reach every voter.");
                }

                if (!IsLeader && TryGetKnownLeaderEndpoint(out var leaderEndpoint))
                {
                    return await transport.RequestAsync(
                        leaderEndpoint!,
                        request,
                        cancellationToken).ConfigureAwait(false);
                }

                var ready = await CommitMemberReadyDescriptorAsync(
                    MembershipWireCodec.DecodeReadyRequest(request),
                    transport,
                    cancellationToken).ConfigureAwait(false);
                return MembershipWireCodec.EncodeReadyResponse(ready);
            }

            throw new InvalidDataException("Unknown membership request frame kind.");
        }

        private static NodeEndpoint GetEndpoint(
            ClusterMembershipSnapshot snapshot,
            NodeReference target)
        {
            if (!snapshot.TryGetMember(target, out var member) || member is null)
            {
                throw new InvalidOperationException(
                    "The replication target is not present in the committed membership view.");
            }

            return member.ClusterEndpoint;
        }

        private bool TryGetKnownLeaderEndpoint(out NodeEndpoint? endpoint)
        {
            lock (log.SyncRoot)
            {
                var leader = knownLeader;
                var snapshot = runtime.Current;
                if (leader is not null
                    && snapshot.TryGetMember(leader, out var member)
                    && member is not null)
                {
                    endpoint = member.ClusterEndpoint;
                    return true;
                }
            }

            endpoint = null;
            return false;
        }

        public Task RunAsync(
            IClusterAuthorityListener listener,
            CancellationToken cancellationToken = default)
        {
            if (listener is null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            var loop = new MembershipControlLoop(
                new LocalControlRound(this),
                proofTracker,
                listener,
                new TimeProviderMembershipControlDelay(timeProvider),
                new Random(),
                new MembershipControlLoopOptions
                {
                    HeartbeatInterval = options.HeartbeatInterval,
                    MinimumRetryDelay = options.MinimumRetryDelay,
                    MaximumRetryDelay = options.MaximumRetryDelay
                });
            return loop.RunAsync(cancellationToken);
        }

        public Task RunAsync(
            IClusterAuthorityListener listener,
            IClusterMembershipTransport transport,
            CancellationToken cancellationToken = default)
        {
            if (listener is null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            if (transport is null)
            {
                throw new ArgumentNullException(nameof(transport));
            }

            var loop = new MembershipControlLoop(
                new NetworkControlRound(this, transport),
                proofTracker,
                listener,
                new TimeProviderMembershipControlDelay(timeProvider),
                new Random(),
                new MembershipControlLoopOptions
                {
                    HeartbeatInterval = options.HeartbeatInterval,
                    MinimumRetryDelay = options.MinimumRetryDelay,
                    MaximumRetryDelay = options.MaximumRetryDelay
                });
            return loop.RunAsync(cancellationToken);
        }

        public async ValueTask<ClusterMembershipSnapshot> RequestPromotionAsync(
            IReadOnlyList<NodeEndpoint> contacts,
            IClusterMembershipTransport transport,
            CancellationToken cancellationToken = default)
        {
            if (contacts is null || contacts.Count == 0)
            {
                throw new ArgumentException("At least one cluster contact is required.", nameof(contacts));
            }

            ClusterMembershipTransportFrame request;
            lock (log.SyncRoot)
            {
                request = MembershipWireCodec.EncodePromoteRequest(
                    Local,
                    runtime.Current.View,
                    log.LastIndex);
            }
            var failures = new List<Exception>();
            for (var i = 0; i < contacts.Count; i++)
            {
                try
                {
                    var response = await transport.RequestAsync(
                        contacts[i],
                        request,
                        cancellationToken).ConfigureAwait(false);
                    var promoted = MembershipWireCodec.DecodePromoteResponse(response);
                    var localCurrent = runtime.Current;
                    if (promoted.Cluster != Local.Cluster
                        || promoted.View != localCurrent.View
                        || !localCurrent.TryGetMember(Local, out var member)
                        || member is null
                        || !member.IsVoter)
                    {
                        throw new InvalidDataException(
                            "The promotion response does not match the locally committed voter view.");
                    }

                    return localCurrent;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            throw new AggregateException("No cluster contact completed learner promotion.", failures);
        }

        public async ValueTask<ClusterMembershipSnapshot> RequestReadyAsync(
            IReadOnlyList<NodeEndpoint> contacts,
            IClusterMembershipTransport transport,
            CancellationToken cancellationToken = default)
        {
            if (!runtime.Current.TryGetMember(Local, out var member) || member is null)
            {
                throw new InvalidOperationException("The local member descriptor is unavailable.");
            }

            return await RequestReadyAsync(
                member,
                contacts,
                transport,
                cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<ClusterMembershipSnapshot> RequestReadyAsync(
            ClusterMember readyDescriptor,
            IReadOnlyList<NodeEndpoint> contacts,
            IClusterMembershipTransport transport,
            CancellationToken cancellationToken = default)
        {
            if (readyDescriptor is null || readyDescriptor.Reference != Local)
            {
                throw new ArgumentException(
                    "The ready descriptor must belong to the exact local incarnation.",
                    nameof(readyDescriptor));
            }

            if (contacts is null || contacts.Count == 0)
            {
                throw new ArgumentException("At least one cluster contact is required.", nameof(contacts));
            }

            var request = MembershipWireCodec.EncodeReadyRequest(readyDescriptor);
            var failures = new List<Exception>();
            for (var i = 0; i < contacts.Count; i++)
            {
                try
                {
                    var response = await transport.RequestAsync(
                        contacts[i],
                        request,
                        cancellationToken).ConfigureAwait(false);
                    var ready = MembershipWireCodec.DecodeReadyResponse(response);
                    var localCurrent = runtime.Current;
                    if (ready.View != localCurrent.View
                        || !localCurrent.TryGetMember(Local, out var member)
                        || member is null
                        || member.State != ClusterMemberState.Ready)
                    {
                        throw new InvalidDataException(
                            "The ready response does not match the locally committed membership view.");
                    }

                    return localCurrent;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            throw new AggregateException("No cluster contact committed the local ready state.", failures);
        }

        private void ExecuteLocalRound(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (log.SyncRoot)
            {
                EnsureLeadership();
                replication.BeginHeartbeat();
                if (!replication.TryIssueQuorumProof(options.ProofValidity, out var proof)
                    || proof is null
                    || !proofTracker.TryAccept(proof))
                {
                    throw new InvalidOperationException(
                        "The local voter could not renew quorum authority.");
                }
            }
        }

        private async ValueTask ExecuteNetworkRoundAsync(
            IClusterMembershipTransport transport,
            CancellationToken cancellationToken)
        {
            if (election.Role != MembershipElectionRole.Leader)
            {
                if (proofTracker.HasAuthority)
                {
                    return;
                }

                MembershipElectionCampaign campaign;
                lock (log.SyncRoot)
                {
                    campaign = election.StartElection();
                }

                var electionView = runtime.Current;
                for (var i = 0; i < campaign.Requests.Count; i++)
                {
                    var request = campaign.Requests[i];
                    try
                    {
                        var response = await transport.RequestAsync(
                            GetEndpoint(electionView, request.Target),
                            MembershipWireCodec.EncodeVoteRequest(request),
                            cancellationToken).ConfigureAwait(false);
                        election.RecordVote(MembershipWireCodec.DecodeVoteResponse(response));
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch
                    {
                        // An unavailable minority must not prevent a live majority from electing.
                    }
                }
            }

            if (election.Role != MembershipElectionRole.Leader)
            {
                throw new InvalidOperationException(
                    "The membership replica did not acquire a voter majority.");
            }

            lock (log.SyncRoot)
            {
                knownLeader = Local;
            }

            MembershipLeaderProposal heartbeat;
            ClusterMembershipSnapshot heartbeatView;
            lock (log.SyncRoot)
            {
                heartbeatView = runtime.Current;
                heartbeat = replication.BeginHeartbeat();
            }

            for (var i = 0; i < heartbeat.Requests.Count; i++)
            {
                var request = heartbeat.Requests[i];
                try
                {
                    var response = await transport.RequestAsync(
                        GetEndpoint(heartbeatView, request.Target),
                        MembershipWireCodec.EncodeAppendRequest(request),
                        cancellationToken).ConfigureAwait(false);
                    replication.RecordReply(MembershipWireCodec.DecodeAppendResponse(response));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Quorum evaluation below decides whether the missed voter is tolerable.
                }
            }

            QuorumProof proof;
            lock (log.SyncRoot)
            {
                if (!replication.TryIssueQuorumProof(options.ProofValidity, out var issued)
                    || issued is null
                    || !proofTracker.TryAccept(issued))
                {
                    throw new InvalidOperationException(
                        "The membership leader could not renew quorum authority.");
                }

                proof = issued;
            }

            for (var i = 0; i < heartbeat.Requests.Count; i++)
            {
                var request = heartbeat.Requests[i];
                try
                {
                    var response = await transport.RequestAsync(
                        GetEndpoint(heartbeatView, request.Target),
                        MembershipWireCodec.EncodeProof(proof),
                        cancellationToken).ConfigureAwait(false);
                    MembershipWireCodec.DecodeProofResponse(response);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Proof delivery is retried on the next heartbeat; authority remains bounded.
                }
            }
        }

        private void EnsureLeadership()
        {
            if (election.Role == MembershipElectionRole.Leader)
            {
                return;
            }

            election.StartElection();
            if (election.Role != MembershipElectionRole.Leader)
            {
                throw new InvalidOperationException(
                    "The membership replica has not acquired leadership.");
            }

            knownLeader = Local;
        }

        private sealed class LocalControlRound : IMembershipControlRound
        {
            private readonly ClusterMembershipNode owner;

            public LocalControlRound(ClusterMembershipNode owner)
            {
                this.owner = owner;
            }

            public ValueTask ExecuteAsync(CancellationToken cancellationToken)
            {
                owner.ExecuteLocalRound(cancellationToken);
                return default;
            }
        }

        private sealed class NetworkControlRound : IMembershipControlRound
        {
            private readonly ClusterMembershipNode owner;
            private readonly IClusterMembershipTransport transport;

            public NetworkControlRound(
                ClusterMembershipNode owner,
                IClusterMembershipTransport transport)
            {
                this.owner = owner;
                this.transport = transport;
            }

            public ValueTask ExecuteAsync(CancellationToken cancellationToken)
            {
                return owner.ExecuteNetworkRoundAsync(transport, cancellationToken);
            }
        }
    }
}
