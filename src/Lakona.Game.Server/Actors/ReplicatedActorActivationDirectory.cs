using System.Text;
using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors.Internal;
using Lakona.Game.Server.Hosting;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Actors;

public sealed class ReplicatedActorActivationDirectory :
    IActorDirectory,
    IActorActivationDirectory,
    IClusterMessageHandler,
    IActorActivationPopulationSource
{
    private const int PartitionCount = 1024;
    private const int ReplicaCount = 3;
    // Rejected is the only send result which proves the remote handler did not
    // execute. Retrying any other outcome could duplicate a state transition.
    private const int RejectedSendAttempts = 3;
    private const string ResolveKind = "_activation_resolve_v2";
    private const string ReplicaResolveKind = "_activation_replica_resolve_v2";
    private const string AcquireKind = "_activation_acquire_v2";
    private const string ReplicateRecordKind = "_activation_replicate_record_v2";
    private const string ReleaseKind = "_activation_release_v2";
    private static readonly RouteKey Route = new("actor-activation:partition");

    private readonly ActivationReplicaStore replica = new();
    private readonly ActorHostingOperationGate operationGate = new();
    private readonly IClusterMembership membership;
    private readonly IExactClusterNodeSender exactSender;
    private readonly IClusterNodeSender replySender;
    private readonly RemoteActorGateway gateway;
    private readonly LocalActorNodeIdentity localNode;
    private readonly TimeSpan timeout;
    private readonly IDistributedWorkAdmissionGate? admissionGate;
    private readonly ActorActivationReplicaDiagnostics diagnostics;

    public ReplicatedActorActivationDirectory(
        IClusterMembership membership,
        IExactClusterNodeSender exactSender,
        IClusterNodeSender replySender,
        RemoteActorGateway gateway,
        LocalActorNodeIdentity localNode,
        RemoteActorOptions? options = null,
        IDistributedWorkAdmissionGate? admissionGate = null,
        ILogger<ReplicatedActorActivationDirectory>? logger = null,
        TimeProvider? timeProvider = null)
    {
        this.membership = membership;
        this.exactSender = exactSender;
        this.replySender = replySender;
        this.gateway = gateway;
        this.localNode = localNode;
        this.admissionGate = admissionGate;
        diagnostics = new ActorActivationReplicaDiagnostics(logger, timeProvider ?? TimeProvider.System);
        timeout = (options ?? new RemoteActorOptions()).DefaultTimeout;
    }

    public async ValueTask<ActorDirectoryRecord?> ResolveAsync(
        ActorId actorId,
        CancellationToken cancellationToken = default)
    {
        var admission = EnterAdmission();
        try
        {
            var reply = await ExecuteAtPrimaryAsync(
                ResolveKind,
                new ActivationRequest { ActorId = actorId.Value },
                cancellationToken).ConfigureAwait(false);
            if (!reply.Succeeded)
            {
                throw new ActorDirectoryUnavailableException(
                    reply.Error ?? "Activation resolve could not reconcile the current replica set.");
            }

            return reply.Record is null || reply.Record.Released
                ? null
                : FromDto(reply.Record).ToDirectoryRecord();
        }
        finally
        {
            ExitAdmission(admission);
        }
    }

    ActorActivationPopulation IActorActivationPopulationSource.ObserveActivationPopulation() =>
        replica.ObservePopulation();

    public async ValueTask<ActorDirectoryRegisterStatus> RegisterAsync(
        ActorId actorId,
        NodeId node,
        CancellationToken cancellationToken = default)
    {
        var snapshot = membership.Current;
        var member = snapshot.Members.SingleOrDefault(item =>
            item.Reference.Node == node && item.State == ClusterMemberState.Ready);
        if (member is null)
        {
            return ActorDirectoryRegisterStatus.Conflict;
        }

        var result = await AcquireAsync(
            actorId,
            member.Reference,
            ActorActivationId.New(),
            cancellationToken).ConfigureAwait(false);
        return result.Acquired
            ? ActorDirectoryRegisterStatus.Registered
            : result.Record.Node == node
                ? ActorDirectoryRegisterStatus.AlreadyRegistered
                : ActorDirectoryRegisterStatus.Conflict;
    }

    public async ValueTask<ActorDirectoryUnregisterStatus> UnregisterAsync(
        ActorId actorId,
        NodeId node,
        CancellationToken cancellationToken = default)
    {
        var existing = await ResolveAsync(actorId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return ActorDirectoryUnregisterStatus.NotFound;
        }

        if (existing.Node != node
            || existing.ActivationId is not ActorActivationId activation)
        {
            return ActorDirectoryUnregisterStatus.OwnershipMismatch;
        }

        return await ReleaseAsync(
            actorId,
            activation,
            existing.Version,
            cancellationToken).ConfigureAwait(false)
            ? ActorDirectoryUnregisterStatus.Unregistered
            : ActorDirectoryUnregisterStatus.OwnershipMismatch;
    }

    public async ValueTask<ActorActivationAcquireResult> AcquireAsync(
        ActorId actorId,
        NodeReference proposedOwner,
        ActorActivationId proposedActivation,
        CancellationToken cancellationToken = default)
    {
        var admission = EnterAdmission();
        try
        {
            var reply = await ExecuteAtPrimaryAsync(
                AcquireKind,
                ActivationRequest.ForAcquire(actorId, proposedOwner, proposedActivation),
                cancellationToken).ConfigureAwait(false);
            if (reply.Record is null || !reply.Succeeded)
            {
                throw new ActorDirectoryUnavailableException(
                    reply.Error ?? "Activation acquire did not reach a replica majority.");
            }

            var record = FromDto(reply.Record);
            if (record.IsReleased)
            {
                throw new ActorDirectoryUnavailableException(
                    "Activation acquire returned a release tombstone.");
            }

            return new ActorActivationAcquireResult(record.ToDirectoryRecord(), reply.Changed);
        }
        finally
        {
            ExitAdmission(admission);
        }
    }

    public async ValueTask<bool> ReleaseAsync(
        ActorId actorId,
        ActorActivationId expectedActivation,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var admission = EnterAdmission();
        try
        {
            var reply = await ExecuteAtPrimaryAsync(
                ReleaseKind,
                new ActivationRequest
                {
                    ActorId = actorId.Value,
                    Activation = expectedActivation.Value,
                    Version = expectedVersion
                },
                cancellationToken).ConfigureAwait(false);
            if (!reply.Succeeded)
            {
                throw new ActorDirectoryUnavailableException(
                    reply.Error ?? "Activation release did not reach a replica majority.");
            }

            return reply.Changed;
        }
        finally
        {
            ExitAdmission(admission);
        }
    }

    public async ValueTask<ClusterSendStatus> HandleAsync(
        ClusterMessage message,
        CancellationToken cancellationToken = default)
    {
        if (message.Kind is not (ResolveKind or ReplicaResolveKind or AcquireKind or ReplicateRecordKind
            or ReleaseKind))
        {
            return ClusterSendStatus.RouteNotFound;
        }

        if (string.IsNullOrWhiteSpace(message.CorrelationId))
        {
            return ClusterSendStatus.Rejected;
        }

        DistributedWorkAdmission admission = default;
        if (admissionGate is not null && !admissionGate.TryEnter(out admission))
        {
            return ClusterSendStatus.Rejected;
        }

        try
        {
            ActivationReply reply;
            try
            {
                var request = JsonSerializer.Deserialize<ActivationRequest>(message.Payload.Span)
                    ?? throw new InvalidOperationException("Activation request is empty.");
                reply = await ExecuteLocalAsync(message.Kind, request, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                reply = new ActivationReply { Error = exception.Message };
            }

            return await RemoteActorGateway.SendReplyAsync(
                replySender,
                localNode.NodeId,
                message.SourceNode,
                message.CorrelationId,
                JsonSerializer.SerializeToUtf8Bytes(reply),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ExitAdmission(admission);
        }
    }

    private DistributedWorkAdmission EnterAdmission()
    {
        if (admissionGate is null)
        {
            return default;
        }

        if (!admissionGate.TryEnter(out var admission))
        {
            throw new ActorDirectoryUnavailableException(
                "Distributed actor work is fenced because quorum authority is unavailable.");
        }

        return admission;
    }

    private void ExitAdmission(DistributedWorkAdmission admission)
    {
        if (admission.IsAdmitted)
        {
            admissionGate!.Exit(admission);
        }
    }

    private async ValueTask<ActivationReply> ExecuteAtPrimaryAsync(
        string kind,
        ActivationRequest request,
        CancellationToken cancellationToken)
    {
        var snapshot = membership.Current;
        var replicas = SelectReplicas(snapshot, request.ActorId);
        if (replicas.Count == 0)
        {
            throw new ActorDirectoryUnavailableException("No ready activation-directory replica exists.");
        }

        return replicas[0].Reference.Node == localNode.NodeId
            ? await ExecuteLocalAsync(kind, request, cancellationToken).ConfigureAwait(false)
            : await SendRequestAsync(replicas[0], snapshot.View, kind, request, cancellationToken)
                .ConfigureAwait(false);
    }

    private async ValueTask<ActivationReply> ExecuteLocalAsync(
        string kind,
        ActivationRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = ActorId.From(request.ActorId);
        if (kind == ReplicaResolveKind)
        {
            var record = replica.Resolve(actorId);
            return new ActivationReply { Succeeded = true, Record = record is null ? null : ToDto(record) };
        }

        if (kind == ReplicateRecordKind)
        {
            var record = request.ToRecord();
            var applied = replica.Apply(record);
            return new ActivationReply
            {
                Succeeded = SameRecord(applied.Record, record),
                Changed = applied.Changed,
                Record = ToDto(applied.Record)
            };
        }

        if (kind == ResolveKind)
        {
            var snapshot = membership.Current;
            var replicas = SelectReplicas(snapshot, request.ActorId);
            var record = await ReadAuthoritativeAsync(actorId, snapshot, cancellationToken)
                .ConfigureAwait(false);
            if (record is not null)
            {
                await RepairReplicasAsync(record, snapshot, replicas, cancellationToken)
                    .ConfigureAwait(false);
            }

            return new ActivationReply
            {
                Succeeded = true,
                Record = record is null || record.IsReleased ? null : ToDto(record)
            };
        }

        await using var operation = await operationGate.EnterAsync(actorId, cancellationToken)
            .ConfigureAwait(false);

        if (kind == AcquireKind)
        {
            var snapshot = membership.Current;
            var replicas = SelectReplicas(snapshot, request.ActorId);
            var proposedOwner = request.ToOwner();
            if (!snapshot.TryGetMember(proposedOwner, out var proposedMember)
                || proposedMember!.State != ClusterMemberState.Ready)
            {
                return new ActivationReply
                {
                    Error = "The proposed actor owner is not an exact Ready member of the committed view."
                };
            }

            var existing = await ReadAuthoritativeAsync(actorId, snapshot, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                await RepairReplicasAsync(existing, snapshot, replicas, cancellationToken)
                    .ConfigureAwait(false);

                // Membership removal is the fencing decision. Until the exact old
                // incarnation disappears from a committed view, sticky ownership wins.
                if (!existing.IsReleased
                    && existing.OwnerReference is not null
                    && snapshot.TryGetMember(existing.OwnerReference, out _))
                {
                    return new ActivationReply
                    {
                        Succeeded = true,
                        Changed = false,
                        Record = ToDto(existing)
                    };
                }
            }

            var version = NextVersion(existing?.Version ?? 0, actorId);
            var record = ActivationReplicaRecord.Active(
                actorId,
                proposedOwner,
                new ActorActivationId(request.Activation),
                version,
                DateTimeOffset.UtcNow);
            if (!await CommitRecordAsync(record, snapshot, replicas, cancellationToken)
                    .ConfigureAwait(false))
            {
                return new ActivationReply { Error = "Activation acquire lacked a replica majority." };
            }

            return new ActivationReply { Succeeded = true, Changed = true, Record = ToDto(record) };
        }

        var releaseSnapshot = membership.Current;
        var releaseReplicas = SelectReplicas(releaseSnapshot, request.ActorId);
        var current = await ReadAuthoritativeAsync(actorId, releaseSnapshot, cancellationToken)
            .ConfigureAwait(false);
        if (current is null || current.IsReleased
            || current.ActivationId != new ActorActivationId(request.Activation)
            || current.Version != request.Version)
        {
            return new ActivationReply { Succeeded = true, Changed = false };
        }

        await RepairReplicasAsync(current, releaseSnapshot, releaseReplicas, cancellationToken)
            .ConfigureAwait(false);
        // Deletion is a versioned state transition. Removing the record would
        // let an older replica resurrect the activation after membership changes.
        var tombstone = ActivationReplicaRecord.Tombstone(
            actorId,
            NextVersion(current.Version, actorId),
            DateTimeOffset.UtcNow);
        if (!await CommitRecordAsync(
                tombstone,
                releaseSnapshot,
                releaseReplicas,
                cancellationToken).ConfigureAwait(false))
        {
            return new ActivationReply { Error = "Activation release lacked a replica majority." };
        }

        return new ActivationReply { Succeeded = true, Changed = true };
    }

    private async ValueTask<ActivationReplicaRecord?> ReadAuthoritativeAsync(
        ActorId actorId,
        ClusterMembershipSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        // A newly Ready member has no record until reconciliation reaches it.
        // Therefore null means "not learned", not "deleted". Querying every
        // Ready member preserves a record written by an older replica set.
        var readers = snapshot.Members
            .Where(static member => member.State == ClusterMemberState.Ready)
            .ToArray();
        var replies = new List<ActivationReplicaRecord?>(readers.Length);
        var request = new ActivationRequest { ActorId = actorId.Value };
        foreach (var reader in readers)
        {
            if (reader.Reference.Node == localNode.NodeId)
            {
                replies.Add(replica.Resolve(actorId));
                continue;
            }

            try
            {
                var reply = await SendRequestAsync(
                    reader, snapshot.View, ReplicaResolveKind, request, cancellationToken)
                    .ConfigureAwait(false);
                if (reply.Succeeded)
                {
                    replies.Add(reply.Record is null ? null : FromDto(reply.Record));
                }
                else
                {
                    diagnostics.Report(
                        ActorActivationReplicaFailurePhase.AuthoritativeRead,
                        reader,
                        snapshot.View,
                        "rejected",
                        exception: null);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                diagnostics.Report(
                    ActorActivationReplicaFailurePhase.AuthoritativeRead,
                    reader,
                    snapshot.View,
                    "exception",
                    exception);
            }
        }

        if (replies.Count != readers.Length)
        {
            throw new ActorDirectoryUnavailableException(
                "Activation read did not reach every Ready member during replica-set reconciliation.");
        }

        var records = replies
            .Where(static record => record is not null)
            .Cast<ActivationReplicaRecord>()
            .ToArray();
        if (records.Length == 0)
        {
            return null;
        }

        var highestVersion = records.Max(static record => record.Version);
        var winners = records
            .Where(record => record.Version == highestVersion)
            .GroupBy(ActivationIdentity.FromRecord)
            .ToArray();
        if (winners.Length != 1)
        {
            throw new ActorDirectoryUnavailableException(
                $"Activation version {highestVersion} has conflicting records for '{actorId.Value}'.");
        }

        return winners[0].First();
    }

    private async ValueTask RepairReplicasAsync(
        ActivationReplicaRecord record,
        ClusterMembershipSnapshot snapshot,
        IReadOnlyList<ClusterMember> replicas,
        CancellationToken cancellationToken)
    {
        replica.Apply(record);
        var request = ActivationRequest.ForRecord(record);
        foreach (var target in SelectPropagationTargets(record, snapshot, replicas))
        {
            if (target.Reference.Node == localNode.NodeId)
            {
                continue;
            }

            try
            {
                var reply = await SendRequestAsync(
                    target, snapshot.View, ReplicateRecordKind, request, cancellationToken)
                    .ConfigureAwait(false);
                if (!reply.Succeeded || reply.Record is null
                    || !SameRecord(FromDto(reply.Record), record))
                {
                    diagnostics.Report(
                        ActorActivationReplicaFailurePhase.ReplicaRepair,
                        target,
                        snapshot.View,
                        "rejected",
                        exception: null);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                diagnostics.Report(
                    ActorActivationReplicaFailurePhase.ReplicaRepair,
                    target,
                    snapshot.View,
                    "exception",
                    exception);
            }
        }
    }

    private async ValueTask<bool> CommitRecordAsync(
        ActivationReplicaRecord record,
        ClusterMembershipSnapshot snapshot,
        IReadOnlyList<ClusterMember> replicas,
        CancellationToken cancellationToken)
    {
        var previous = replica.Resolve(record.ActorId);
        ReplicaApplyResult local;
        try
        {
            local = replica.Apply(record);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (!SameRecord(local.Record, record))
        {
            return false;
        }

        var acknowledgements = 1;
        var request = ActivationRequest.ForRecord(record);
        for (var i = 1; i < replicas.Count; i++)
        {
            try
            {
                var reply = await SendRequestAsync(
                    replicas[i], snapshot.View, ReplicateRecordKind, request, cancellationToken)
                    .ConfigureAwait(false);
                if (reply.Succeeded && reply.Record is not null
                    && SameRecord(FromDto(reply.Record), record))
                {
                    acknowledgements++;
                }
                else
                {
                    diagnostics.Report(
                        ActorActivationReplicaFailurePhase.QuorumCommit,
                        replicas[i],
                        snapshot.View,
                        "rejected",
                        exception: null);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                diagnostics.Report(
                    ActorActivationReplicaFailurePhase.QuorumCommit,
                    replicas[i],
                    snapshot.View,
                    "exception",
                    exception);
            }
        }

        if (acknowledgements >= replicas.Count / 2 + 1)
        {
            await PropagateAdditionalCopiesAsync(
                record,
                snapshot,
                replicas,
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        replica.TryRestore(record, previous);
        return false;
    }

    private async ValueTask PropagateAdditionalCopiesAsync(
        ActivationReplicaRecord record,
        ClusterMembershipSnapshot snapshot,
        IReadOnlyList<ClusterMember> replicas,
        CancellationToken cancellationToken)
    {
        var replicaNodes = replicas
            .Select(static member => member.Reference.Node)
            .ToHashSet();
        var request = ActivationRequest.ForRecord(record);
        foreach (var target in SelectPropagationTargets(record, snapshot, replicas))
        {
            if (target.Reference.Node == localNode.NodeId
                || replicaNodes.Contains(target.Reference.Node))
            {
                continue;
            }

            try
            {
                var reply = await SendRequestAsync(
                    target,
                    snapshot.View,
                    ReplicateRecordKind,
                    request,
                    cancellationToken).ConfigureAwait(false);
                if (!reply.Succeeded || reply.Record is null
                    || !SameRecord(FromDto(reply.Record), record))
                {
                    diagnostics.Report(
                        ActorActivationReplicaFailurePhase.AdditionalPropagation,
                        target,
                        snapshot.View,
                        "rejected",
                        exception: null);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                diagnostics.Report(
                    ActorActivationReplicaFailurePhase.AdditionalPropagation,
                    target,
                    snapshot.View,
                    "exception",
                    exception);
            }
        }
    }

    private static IReadOnlyList<ClusterMember> SelectPropagationTargets(
        ActivationReplicaRecord record,
        ClusterMembershipSnapshot snapshot,
        IReadOnlyList<ClusterMember> replicas)
    {
        if (record.IsReleased)
        {
            // Every current member may hold a record from an older rendezvous
            // replica set. Spreading the tombstone prevents that stale copy from
            // becoming authoritative after later contraction.
            return snapshot.Members
                .Where(static member => member.State == ClusterMemberState.Ready)
                .ToArray();
        }

        if (record.OwnerReference is null
            || replicas.Any(member => member.Reference == record.OwnerReference)
            || !snapshot.TryGetMember(record.OwnerReference, out var owner)
            || owner!.State != ClusterMemberState.Ready)
        {
            return replicas;
        }

        return [.. replicas, owner];
    }

    private async ValueTask<ActivationReply> SendRequestAsync(
        ClusterMember target,
        MembershipViewId view,
        string kind,
        ActivationRequest request,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < RejectedSendAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var correlation = Guid.NewGuid().ToString("N");
            var pending = gateway.RegisterPendingAsync(correlation, timeout, cancellationToken);
            var message = new ClusterMessage(
                Route, kind, JsonSerializer.SerializeToUtf8Bytes(request),
                DateTimeOffset.UtcNow.Add(timeout), localNode.NodeId, correlation,
                orderedBy: request.ActorId);
            var status = await exactSender.SendAsync(
                target.Reference, view, Route, message, cancellationToken).ConfigureAwait(false);
            if (status == ClusterSendStatus.Accepted)
            {
                var payload = await pending.ConfigureAwait(false);
                return JsonSerializer.Deserialize<ActivationReply>(payload.Span)
                    ?? throw new ActorDirectoryUnavailableException("Activation replica returned no reply.");
            }

            gateway.TryCancelPending(correlation);
            if (status != ClusterSendStatus.Rejected || attempt == RejectedSendAttempts - 1)
            {
                throw new ActorDirectoryUnavailableException(
                    $"Activation replica send failed with status '{status}'.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25 * (1 << attempt)), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new InvalidOperationException("Rejected-send retry loop completed unexpectedly.");
    }

    private static IReadOnlyList<ClusterMember> SelectReplicas(
        ClusterMembershipSnapshot snapshot,
        string actorId)
    {
        var partition = (int)(Hash(actorId) % PartitionCount);
        return snapshot.Members
            .Where(static member => member.State == ClusterMemberState.Ready)
            .OrderByDescending(member => Hash(partition + "\0" + member.Reference.ToString()))
            .ThenBy(member => member.Reference.Node.Value, StringComparer.Ordinal)
            .Take(ReplicaCount)
            .ToArray();
    }

    private static ulong Hash(string value)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        var bytes = Encoding.UTF8.GetBytes(value);
        for (var i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= prime;
        }

        return hash;
    }

    private static long NextVersion(long previous, ActorId actorId)
    {
        if (previous == long.MaxValue)
        {
            throw new ActorDirectoryUnavailableException(
                $"Actor activation version is exhausted for '{actorId.Value}'.");
        }

        return previous + 1;
    }

    private static bool SameRecord(
        ActivationReplicaRecord left,
        ActivationReplicaRecord right) =>
        left.ActorId == right.ActorId
        && left.OwnerReference == right.OwnerReference
        && left.ActivationId == right.ActivationId
        && left.Version == right.Version
        && left.IsReleased == right.IsReleased;

    private static ActivationRecordDto ToDto(ActivationReplicaRecord record) => new()
    {
        ActorId = record.ActorId.Value,
        Cluster = record.OwnerReference?.Cluster.Value ?? Guid.Empty,
        Node = record.OwnerReference?.Node.Value ?? string.Empty,
        Incarnation = record.OwnerReference?.Incarnation.Value ?? Guid.Empty,
        Activation = record.ActivationId?.Value ?? Guid.Empty,
        Version = record.Version,
        UpdatedAt = record.UpdatedAt,
        Released = record.IsReleased
    };

    private static ActivationReplicaRecord FromDto(ActivationRecordDto dto)
    {
        var actorId = ActorId.From(dto.ActorId);
        return dto.Released
            ? ActivationReplicaRecord.Tombstone(actorId, dto.Version, dto.UpdatedAt)
            : ActivationReplicaRecord.Active(
                actorId,
                new NodeReference(
                    new ClusterIncarnationId(dto.Cluster),
                    new NodeId(dto.Node),
                    new NodeIncarnationId(dto.Incarnation)),
                new ActorActivationId(dto.Activation),
                dto.Version,
                dto.UpdatedAt);
    }

    private sealed class ActivationRequest
    {
        public string ActorId { get; set; } = string.Empty;
        public Guid Cluster { get; set; }
        public string Node { get; set; } = string.Empty;
        public Guid Incarnation { get; set; }
        public Guid Activation { get; set; }
        public long Version { get; set; }

        public static ActivationRequest ForAcquire(
            ActorId actorId,
            NodeReference owner,
            ActorActivationId activation) => new()
            {
                ActorId = actorId.Value,
                Cluster = owner.Cluster.Value,
                Node = owner.Node.Value,
                Incarnation = owner.Incarnation.Value,
                Activation = activation.Value
            };

        public static ActivationRequest ForRecord(ActivationReplicaRecord record) => new()
        {
            ActorId = record.ActorId.Value,
            Cluster = record.OwnerReference?.Cluster.Value ?? Guid.Empty,
            Node = record.OwnerReference?.Node.Value ?? string.Empty,
            Incarnation = record.OwnerReference?.Incarnation.Value ?? Guid.Empty,
            Activation = record.ActivationId?.Value ?? Guid.Empty,
            Version = record.Version,
            UpdatedAt = record.UpdatedAt,
            Released = record.IsReleased
        };

        public NodeReference ToOwner() => new(
            new ClusterIncarnationId(Cluster),
            new NodeId(Node),
            new NodeIncarnationId(Incarnation));

        public ActivationReplicaRecord ToRecord()
        {
            var actorId = global::Lakona.Game.Server.Actors.ActorId.From(ActorId);
            return Released
                ? ActivationReplicaRecord.Tombstone(actorId, Version, UpdatedAt)
                : ActivationReplicaRecord.Active(
                    actorId,
                    ToOwner(),
                    new ActorActivationId(Activation),
                    Version,
                    UpdatedAt);
        }

        public DateTimeOffset UpdatedAt { get; set; }

        public bool Released { get; set; }
    }

    private readonly record struct ActivationIdentity(
        NodeReference? Owner,
        ActorActivationId? Activation,
        long Version,
        bool Released)
    {
        public static ActivationIdentity FromRecord(ActivationReplicaRecord record) => new(
            record.OwnerReference,
            record.ActivationId,
            record.Version,
            record.IsReleased);
    }

    private sealed class ActivationReply
    {
        public bool Succeeded { get; set; }
        public bool Changed { get; set; }
        public ActivationRecordDto? Record { get; set; }
        public string? Error { get; set; }
    }

    private sealed class ActivationRecordDto
    {
        public string ActorId { get; set; } = string.Empty;
        public Guid Cluster { get; set; }
        public string Node { get; set; } = string.Empty;
        public Guid Incarnation { get; set; }
        public Guid Activation { get; set; }
        public long Version { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public bool Released { get; set; }
    }

    private sealed record ActivationReplicaRecord(
        ActorId ActorId,
        NodeReference? OwnerReference,
        ActorActivationId? ActivationId,
        long Version,
        DateTimeOffset UpdatedAt)
    {
        public bool IsReleased => OwnerReference is null;

        public static ActivationReplicaRecord Active(
            ActorId actorId,
            NodeReference owner,
            ActorActivationId activationId,
            long version,
            DateTimeOffset updatedAt) =>
            new(actorId, owner, activationId, version, updatedAt);

        public static ActivationReplicaRecord Tombstone(
            ActorId actorId,
            long version,
            DateTimeOffset updatedAt) =>
            new(actorId, null, null, version, updatedAt);

        public ActorDirectoryRecord ToDirectoryRecord()
        {
            if (OwnerReference is null || ActivationId is not ActorActivationId activation)
            {
                throw new InvalidOperationException(
                    $"Released activation '{ActorId.Value}' has no public directory record.");
            }

            return new ActorDirectoryRecord(
                ActorId,
                OwnerReference,
                activation,
                Version,
                UpdatedAt);
        }
    }

    private sealed class ActivationReplicaStore
    {
        private readonly object gate = new();
        private readonly Dictionary<ActorId, ActivationReplicaRecord> records = new();
        private int activeCount;
        private int releasedCount;

        public ActivationReplicaRecord? Resolve(ActorId actorId)
        {
            lock (gate)
            {
                records.TryGetValue(actorId, out var record);
                return record;
            }
        }

        public ReplicaApplyResult Apply(ActivationReplicaRecord incoming)
        {
            lock (gate)
            {
                records.TryGetValue(incoming.ActorId, out var existing);
                if (existing is not null)
                {
                    if (existing.Version > incoming.Version)
                    {
                        return new ReplicaApplyResult(existing, false);
                    }

                    if (existing.Version == incoming.Version)
                    {
                        if (!SameRecord(existing, incoming))
                        {
                            throw new InvalidOperationException(
                                $"Actor activation version {incoming.Version} conflicts for '{incoming.ActorId.Value}'.");
                        }

                        return new ReplicaApplyResult(existing, false);
                    }
                }

                records[incoming.ActorId] = incoming;
                ApplyPopulationTransition(existing, incoming);
                return new ReplicaApplyResult(incoming, true);
            }
        }

        public ActorActivationPopulation ObservePopulation()
        {
            lock (gate)
            {
                return new ActorActivationPopulation(
                    activeCount,
                    records.Count,
                    releasedCount);
            }
        }

        public void TryRestore(
            ActivationReplicaRecord attempted,
            ActivationReplicaRecord? previous)
        {
            lock (gate)
            {
                if (!records.TryGetValue(attempted.ActorId, out var current)
                    || !SameRecord(current, attempted))
                {
                    return;
                }

                if (previous is null)
                {
                    records.Remove(attempted.ActorId);
                    RemoveFromPopulation(current);
                }
                else
                {
                    records[attempted.ActorId] = previous;
                    ApplyPopulationTransition(current, previous);
                }
            }
        }

        private void ApplyPopulationTransition(
            ActivationReplicaRecord? previous,
            ActivationReplicaRecord current)
        {
            if (previous is null)
            {
                AddToPopulation(current);
                return;
            }

            if (previous.IsReleased == current.IsReleased)
            {
                return;
            }

            RemoveFromPopulation(previous);
            AddToPopulation(current);
        }

        private void AddToPopulation(ActivationReplicaRecord record)
        {
            if (record.IsReleased)
            {
                releasedCount++;
            }
            else
            {
                activeCount++;
            }
        }

        private void RemoveFromPopulation(ActivationReplicaRecord record)
        {
            if (record.IsReleased)
            {
                releasedCount--;
            }
            else
            {
                activeCount--;
            }
        }
    }

    private readonly record struct ReplicaApplyResult(
        ActivationReplicaRecord Record,
        bool Changed);
}
