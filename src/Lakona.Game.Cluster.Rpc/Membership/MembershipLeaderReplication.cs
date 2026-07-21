using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    internal sealed class MembershipAppendReply
    {
        public MembershipAppendReply(
            NodeReference source,
            NodeReference target,
            long term,
            MembershipViewId view,
            long sequence,
            bool accepted,
            long matchIndex)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            if (term < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(term));
            }

            if (sequence <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sequence));
            }

            if (matchIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(matchIndex));
            }

            Term = term;
            View = view;
            Sequence = sequence;
            Accepted = accepted;
            MatchIndex = matchIndex;
        }

        public NodeReference Source { get; }

        public NodeReference Target { get; }

        public long Term { get; }

        public MembershipViewId View { get; }

        public long Sequence { get; }

        public bool Accepted { get; }

        public long MatchIndex { get; }
    }

    internal sealed class MembershipLeaderProposal
    {
        public MembershipLeaderProposal(
            long sequence,
            IReadOnlyList<MembershipAppendRequest> requests)
        {
            Sequence = sequence;
            Requests = new ReadOnlyCollection<MembershipAppendRequest>(
                new List<MembershipAppendRequest>(requests));
        }

        public long Sequence { get; }

        public IReadOnlyList<MembershipAppendRequest> Requests { get; }
    }

    internal sealed class MembershipLeaderReplication
    {
        private readonly object gate;
        private readonly NodeReference local;
        private readonly IClusterMembership membership;
        private readonly MembershipElectionState election;
        private readonly MembershipReplicatedLog log;
        private readonly Dictionary<NodeReference, long> matchIndexes =
            new Dictionary<NodeReference, long>();
        private readonly Dictionary<NodeReference, MembershipViewId> learnerViews =
            new Dictionary<NodeReference, MembershipViewId>();
        private readonly Dictionary<NodeReference, MembershipViewId> replicaViews =
            new Dictionary<NodeReference, MembershipViewId>();
        private readonly Dictionary<NodeReference, MembershipViewId> requestViews =
            new Dictionary<NodeReference, MembershipViewId>();
        private readonly HashSet<NodeReference> currentRoundAcknowledgements =
            new HashSet<NodeReference>();
        private long sequence;
        private long lastProofSequence;
        private ClusterIncarnationId roundCluster;
        private MembershipViewId roundView;
        private long roundTerm;
        private HashSet<NodeReference>? jointOldVoters;
        private HashSet<NodeReference>? jointNewVoters;

        public MembershipLeaderReplication(
            NodeReference local,
            IClusterMembership membership,
            MembershipElectionState election,
            MembershipReplicatedLog log)
        {
            this.local = local ?? throw new ArgumentNullException(nameof(local));
            this.membership = membership ?? throw new ArgumentNullException(nameof(membership));
            this.election = election ?? throw new ArgumentNullException(nameof(election));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            gate = this.log.SyncRoot;
        }

        public MembershipLeaderProposal Propose(
            string commandKind,
            ReadOnlyMemory<byte> payload)
        {
            lock (gate)
            {
                var snapshot = RequireLeadership();
                if (log.CommitIndex != log.LastIndex)
                {
                    throw new InvalidOperationException(
                        "The initial membership leader implementation permits one uncommitted proposal at a time.");
                }

                BeginRound(snapshot);
                var previousIndex = log.LastIndex;
                var previousTerm = log.LastTerm;
                var entry = new MembershipLogEntry(
                    previousIndex + 1,
                    election.CurrentTerm,
                    commandKind,
                    payload);
                var localAppend = log.AppendFromLeader(new MembershipAppendBatch(
                    previousIndex,
                    previousTerm,
                    log.CommitIndex,
                    new[] { entry }));
                if (localAppend.Status != MembershipAppendStatus.Accepted)
                {
                    throw new InvalidOperationException(
                        $"Leader could not append its local membership proposal: {localAppend.Status}.");
                }

                matchIndexes[local] = entry.Index;

                var requests = new List<MembershipAppendRequest>();
                for (var i = 0; i < snapshot.Members.Count; i++)
                {
                    var member = snapshot.Members[i];
                    if (!member.IsVoter || member.Reference == local)
                    {
                        continue;
                    }

                    var request = new MembershipAppendRequest(
                        local,
                        member.Reference,
                        election.CurrentTerm,
                        snapshot.View,
                        sequence,
                        new MembershipAppendBatch(
                            previousIndex,
                            previousTerm,
                            log.CommitIndex,
                            new[] { entry }));
                    requests.Add(request);
                    requestViews[member.Reference] = request.View;
                }

                TryAdvanceCommit(snapshot, entry.Index);
                return new MembershipLeaderProposal(sequence, requests);
            }
        }

        public MembershipLeaderProposal ProposeJointConfiguration(
            string commandKind,
            ReadOnlyMemory<byte> payload,
            ClusterMembershipSnapshot nextSnapshot)
        {
            if (nextSnapshot is null)
            {
                throw new ArgumentNullException(nameof(nextSnapshot));
            }

            lock (gate)
            {
                var current = RequireLeadership();
                if (nextSnapshot.Cluster != current.Cluster
                    || nextSnapshot.View.Value != current.View.Value + 1)
                {
                    throw new InvalidOperationException(
                        "A joint configuration must be the next view of the current cluster.");
                }

                if (log.CommitIndex != log.LastIndex)
                {
                    throw new InvalidOperationException(
                        "A joint configuration cannot replace an in-flight proposal.");
                }

                BeginRound(current);
                jointOldVoters = CollectVoters(current);
                jointNewVoters = CollectVoters(nextSnapshot);
                if (!jointOldVoters.Contains(local) || !jointNewVoters.Contains(local))
                {
                    throw new InvalidOperationException(
                        "The proposing leader must be present in both joint voter sets.");
                }

                var previousIndex = log.LastIndex;
                var previousTerm = log.LastTerm;
                var entry = new MembershipLogEntry(
                    previousIndex + 1,
                    election.CurrentTerm,
                    commandKind,
                    payload);
                var localAppend = log.AppendFromLeader(new MembershipAppendBatch(
                    previousIndex,
                    previousTerm,
                    log.CommitIndex,
                    new[] { entry }));
                if (localAppend.Status != MembershipAppendStatus.Accepted)
                {
                    throw new InvalidOperationException(
                        $"Leader could not append its joint membership proposal: {localAppend.Status}.");
                }

                matchIndexes[local] = entry.Index;
                var targets = new HashSet<NodeReference>(jointOldVoters);
                targets.UnionWith(jointNewVoters);
                targets.Remove(local);
                var requests = new List<MembershipAppendRequest>(targets.Count);
                foreach (var target in targets)
                {
                    var request = new MembershipAppendRequest(
                        local,
                        target,
                        election.CurrentTerm,
                        current.View,
                        sequence,
                        new MembershipAppendBatch(
                            previousIndex,
                            previousTerm,
                            log.CommitIndex,
                            new[] { entry }));
                    requests.Add(request);
                    requestViews[target] = request.View;
                }

                TryAdvanceCommit(current, entry.Index);
                return new MembershipLeaderProposal(sequence, requests);
            }
        }

        public MembershipLeaderProposal BeginHeartbeat()
        {
            lock (gate)
            {
                var snapshot = RequireLeadership();
                if (log.CommitIndex != log.LastIndex)
                {
                    throw new InvalidOperationException(
                        "A heartbeat round cannot replace an in-flight membership proposal.");
                }

                BeginRound(snapshot);
                matchIndexes[local] = log.LastIndex;

                var requests = new List<MembershipAppendRequest>();
                for (var i = 0; i < snapshot.Members.Count; i++)
                {
                    var member = snapshot.Members[i];
                    if (!member.IsVoter || member.Reference == local)
                    {
                        continue;
                    }

                    var matchIndex = matchIndexes.TryGetValue(member.Reference, out var knownMatch)
                        ? Math.Min(knownMatch, log.CommitIndex)
                        : log.CommitIndex;
                    var peerView = replicaViews.TryGetValue(member.Reference, out var knownView)
                        ? knownView
                        : snapshot.View;
                    var request = new MembershipAppendRequest(
                        local,
                        member.Reference,
                        election.CurrentTerm,
                        peerView,
                        sequence,
                        log.CreateCommittedBatchAfter(matchIndex));
                    requests.Add(request);
                    requestViews[member.Reference] = request.View;
                }

                return new MembershipLeaderProposal(sequence, requests);
            }
        }

        public bool RecordReply(MembershipAppendReply reply)
        {
            if (reply is null)
            {
                throw new ArgumentNullException(nameof(reply));
            }

            lock (gate)
            {
                var snapshot = membership.Current;
                if (reply.Target != local
                    || reply.Source.Cluster != snapshot.Cluster
                    || !IsCurrentRoundVoter(snapshot, reply.Source))
                {
                    return false;
                }

                if (reply.Term > election.CurrentTerm)
                {
                    election.ObserveLeader(reply.Term);
                    currentRoundAcknowledgements.Clear();
                    return false;
                }

                if (election.Role != MembershipElectionRole.Leader
                    || reply.Sequence != sequence
                    || reply.MatchIndex > log.LastIndex
                    || !requestViews.TryGetValue(reply.Source, out var requestedView))
                {
                    return false;
                }

                replicaViews[reply.Source] = reply.View;
                if (!reply.Accepted)
                {
                    var previous = matchIndexes.TryGetValue(reply.Source, out var knownMatch)
                        ? knownMatch
                        : log.CommitIndex;
                    matchIndexes[reply.Source] = reply.View == requestedView && previous > 0
                        ? Math.Min(reply.MatchIndex, previous - 1)
                        : Math.Min(reply.MatchIndex, previous);
                    return false;
                }

                if (reply.Term != election.CurrentTerm)
                {
                    return false;
                }

                matchIndexes[reply.Source] = reply.MatchIndex;
                if (reply.View != snapshot.View
                    || reply.MatchIndex < log.CommitIndex
                    || !currentRoundAcknowledgements.Add(reply.Source))
                {
                    return false;
                }

                return TryAdvanceCommit(snapshot, log.LastIndex);
            }
        }

        public void RecordLearnerTransfer(
            NodeReference learner,
            MembershipViewId transferredView)
        {
            if (learner is null)
            {
                throw new ArgumentNullException(nameof(learner));
            }

            lock (gate)
            {
                matchIndexes[learner] = log.CommitIndex;
                learnerViews[learner] = transferredView;
            }
        }

        public void RecordLearnerProgress(
            NodeReference learner,
            MembershipViewId learnerView,
            long learnerMatchIndex)
        {
            if (learner is null)
            {
                throw new ArgumentNullException(nameof(learner));
            }

            lock (gate)
            {
                var snapshot = RequireLeadership();
                if (!snapshot.TryGetMember(learner, out var member)
                    || member is null
                    || member.IsVoter
                    || member.State != ClusterMemberState.Joining)
                {
                    throw new InvalidOperationException(
                        "Only a committed non-voting learner can report promotion progress.");
                }

                if (learnerView.CompareTo(snapshot.View) > 0
                    || learnerMatchIndex <= 0
                    || learnerMatchIndex > log.CommitIndex)
                {
                    throw new InvalidOperationException(
                        "The learner reported a membership position beyond the leader commit.");
                }

                matchIndexes[learner] = learnerMatchIndex;
                learnerViews[learner] = learnerView;
            }
        }

        public MembershipAppendRequest? CreateLearnerCatchUpRequest(NodeReference learner)
        {
            if (learner is null)
            {
                throw new ArgumentNullException(nameof(learner));
            }

            lock (gate)
            {
                var snapshot = RequireLeadership();
                if (!snapshot.TryGetMember(learner, out var member)
                    || member is null
                    || member.IsVoter
                    || member.State != ClusterMemberState.Joining)
                {
                    throw new InvalidOperationException(
                        "Only a committed non-voting learner can be caught up.");
                }

                if (!matchIndexes.TryGetValue(learner, out var matchIndex))
                {
                    throw new InvalidOperationException(
                        "The learner has no recorded join transfer position.");
                }

                if (matchIndex >= log.CommitIndex
                    && learnerViews.TryGetValue(learner, out var caughtUpView)
                    && caughtUpView == snapshot.View)
                {
                    return null;
                }

                if (!learnerViews.TryGetValue(learner, out var learnerView))
                {
                    throw new InvalidOperationException(
                        "The learner has no recorded join transfer view.");
                }

                BeginRound(snapshot);
                var batch = log.CreateCommittedBatchAfter(matchIndex);
                if (batch.Entries.Count > 0
                    && batch.Entries[batch.Entries.Count - 1].Index != log.CommitIndex)
                {
                    throw new MembershipSnapshotRequiredException(log.SnapshotIndex);
                }

                return new MembershipAppendRequest(
                    local,
                    learner,
                    election.CurrentTerm,
                    learnerView,
                    sequence,
                    batch);
            }
        }

        public bool RecordLearnerCatchUpReply(
            NodeReference learner,
            MembershipAppendReply reply)
        {
            if (learner is null)
            {
                throw new ArgumentNullException(nameof(learner));
            }

            if (reply is null)
            {
                throw new ArgumentNullException(nameof(reply));
            }

            lock (gate)
            {
                var snapshot = membership.Current;
                if (reply.Source != learner
                    || reply.Target != local
                    || reply.Source.Cluster != snapshot.Cluster
                    || !learnerViews.TryGetValue(learner, out var learnerView)
                    || reply.Sequence != sequence
                    || reply.MatchIndex > log.CommitIndex)
                {
                    return false;
                }

                if (reply.Term > election.CurrentTerm)
                {
                    election.ObserveLeader(reply.Term);
                    return false;
                }

                if (!reply.Accepted)
                {
                    var previous = matchIndexes[learner];
                    learnerViews[learner] = reply.View;
                    matchIndexes[learner] = reply.View == learnerView && previous > 0
                        ? Math.Min(reply.MatchIndex, previous - 1)
                        : Math.Min(reply.MatchIndex, previous);
                    return true;
                }

                if (reply.Term != election.CurrentTerm)
                {
                    return false;
                }

                matchIndexes[learner] = reply.MatchIndex;
                learnerViews[learner] = reply.View;
                return true;
            }
        }

        public bool TryIssueQuorumProof(TimeSpan validFor, out QuorumProof? proof)
        {
            lock (gate)
            {
                var snapshot = membership.Current;
                if (election.Role != MembershipElectionRole.Leader
                    || sequence == 0
                    || sequence == lastProofSequence
                    || snapshot.Cluster != roundCluster
                    || snapshot.View != roundView
                    || election.CurrentTerm != roundTerm
                    || !HasMajority(
                        snapshot,
                        CountCurrentAcknowledgements(snapshot)))
                {
                    proof = null;
                    return false;
                }

                proof = new QuorumProof(
                    snapshot.Cluster,
                    election.CurrentTerm,
                    snapshot.View,
                    sequence,
                    validFor);
                lastProofSequence = sequence;
                return true;
            }
        }

        private ClusterMembershipSnapshot RequireLeadership()
        {
            var snapshot = membership.Current;
            if (election.Role != MembershipElectionRole.Leader
                || !snapshot.TryGetMember(local, out var member)
                || member is null
                || !member.IsVoter)
            {
                throw new InvalidOperationException(
                    "Only the current committed voter leader can propose membership changes.");
            }

            return snapshot;
        }

        private void BeginRound(ClusterMembershipSnapshot snapshot)
        {
            if (sequence == long.MaxValue)
            {
                throw new TerminalMembershipException("Replication sequence is exhausted.");
            }

            sequence++;
            roundCluster = snapshot.Cluster;
            roundView = snapshot.View;
            roundTerm = election.CurrentTerm;
            currentRoundAcknowledgements.Clear();
            currentRoundAcknowledgements.Add(local);
            requestViews.Clear();
            jointOldVoters = null;
            jointNewVoters = null;
        }

        private bool TryAdvanceCommit(ClusterMembershipSnapshot snapshot, long candidateIndex)
        {
            if (jointOldVoters is not null && jointNewVoters is not null)
            {
                return HasMajority(jointOldVoters, candidateIndex)
                    && HasMajority(jointNewVoters, candidateIndex)
                    && log.AdvanceLeaderCommit(candidateIndex, election.CurrentTerm);
            }

            var replicated = 0;
            for (var i = 0; i < snapshot.Members.Count; i++)
            {
                var member = snapshot.Members[i];
                if (member.IsVoter
                    && matchIndexes.TryGetValue(member.Reference, out var matchIndex)
                    && matchIndex >= candidateIndex)
                {
                    replicated++;
                }
            }

            return HasMajority(snapshot, replicated)
                && log.AdvanceLeaderCommit(candidateIndex, election.CurrentTerm);
        }

        private bool IsCurrentRoundVoter(
            ClusterMembershipSnapshot snapshot,
            NodeReference reference)
        {
            if (jointOldVoters is not null && jointNewVoters is not null)
            {
                return jointOldVoters.Contains(reference) || jointNewVoters.Contains(reference);
            }

            return snapshot.TryGetMember(reference, out var voter)
                && voter is not null
                && voter.IsVoter;
        }

        private bool HasMajority(HashSet<NodeReference> voters, long candidateIndex)
        {
            var replicated = 0;
            foreach (var voter in voters)
            {
                if (matchIndexes.TryGetValue(voter, out var matchIndex)
                    && matchIndex >= candidateIndex)
                {
                    replicated++;
                }
            }

            return replicated >= voters.Count / 2 + 1;
        }

        private static HashSet<NodeReference> CollectVoters(ClusterMembershipSnapshot snapshot)
        {
            var voters = new HashSet<NodeReference>();
            for (var i = 0; i < snapshot.Members.Count; i++)
            {
                if (snapshot.Members[i].IsVoter)
                {
                    voters.Add(snapshot.Members[i].Reference);
                }
            }

            return voters;
        }

        private static bool HasMajority(
            ClusterMembershipSnapshot snapshot,
            int acknowledgements)
        {
            var voters = 0;
            for (var i = 0; i < snapshot.Members.Count; i++)
            {
                if (snapshot.Members[i].IsVoter)
                {
                    voters++;
                }
            }

            return acknowledgements >= voters / 2 + 1;
        }

        private int CountCurrentAcknowledgements(ClusterMembershipSnapshot snapshot)
        {
            var count = 0;
            for (var i = 0; i < snapshot.Members.Count; i++)
            {
                var member = snapshot.Members[i];
                if (member.IsVoter
                    && currentRoundAcknowledgements.Contains(member.Reference))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
