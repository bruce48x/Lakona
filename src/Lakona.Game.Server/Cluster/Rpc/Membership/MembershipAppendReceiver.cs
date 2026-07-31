using System;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    internal sealed class MembershipAppendRequest
    {
        public MembershipAppendRequest(
            NodeReference source,
            NodeReference target,
            long term,
            MembershipViewId view,
            long sequence,
            MembershipAppendBatch batch)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            if (term <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(term), "Append term must be positive.");
            }

            if (sequence <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sequence),
                    "Append sequence must be positive.");
            }

            Term = term;
            View = view;
            Sequence = sequence;
            Batch = batch ?? throw new ArgumentNullException(nameof(batch));
        }

        public NodeReference Source { get; }

        public NodeReference Target { get; }

        public long Term { get; }

        public MembershipViewId View { get; }

        public long Sequence { get; }

        public MembershipAppendBatch Batch { get; }
    }

    internal sealed class MembershipSnapshotInstallRequest
    {
        public MembershipSnapshotInstallRequest(
            NodeReference source,
            NodeReference target,
            long term,
            MembershipViewId view,
            long sequence,
            ClusterMembershipTransfer transfer)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            if (term <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(term));
            }

            if (sequence <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sequence));
            }

            Term = term;
            View = view;
            Sequence = sequence;
            Transfer = transfer ?? throw new ArgumentNullException(nameof(transfer));
        }

        public NodeReference Source { get; }

        public NodeReference Target { get; }

        public long Term { get; }

        public MembershipViewId View { get; }

        public long Sequence { get; }

        public ClusterMembershipTransfer Transfer { get; }
    }

    internal enum MembershipAppendReceiveStatus
    {
        Accepted = 0,
        IdentityMismatch = 1,
        ViewMismatch = 2,
        StaleTerm = 3,
        LogRejected = 4
    }

    internal sealed class MembershipAppendReceiveResult
    {
        public MembershipAppendReceiveResult(
            MembershipAppendReceiveStatus status,
            long term,
            long matchIndex,
            MembershipAppendStatus? logStatus = null)
        {
            Status = status;
            Term = term;
            MatchIndex = matchIndex;
            LogStatus = logStatus;
        }

        public MembershipAppendReceiveStatus Status { get; }

        public long Term { get; }

        public long MatchIndex { get; }

        public MembershipAppendStatus? LogStatus { get; }
    }

    internal sealed class MembershipAppendReceiver
    {
        private readonly object gate;
        private readonly NodeReference local;
        private readonly IClusterMembership membership;
        private readonly MembershipElectionState election;
        private readonly MembershipReplicatedLog log;

        public MembershipAppendReceiver(
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

        public MembershipAppendReceiveResult Append(MembershipAppendRequest request)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            lock (gate)
            {
                var snapshot = membership.Current;
                if (snapshot.Cluster != local.Cluster
                    || request.Source.Cluster != snapshot.Cluster
                    || request.Target != local
                    || !snapshot.TryGetMember(local, out _)
                    || !snapshot.TryGetMember(request.Source, out var source)
                    || source is null
                    || !source.IsVoter
                    || source.State == ClusterMemberState.Draining
                    || source.State == ClusterMemberState.Fenced)
                {
                    return Result(MembershipAppendReceiveStatus.IdentityMismatch);
                }

                if (request.View != snapshot.View)
                {
                    return Result(MembershipAppendReceiveStatus.ViewMismatch);
                }

                if (!election.ObserveLeader(request.Term))
                {
                    return Result(MembershipAppendReceiveStatus.StaleTerm);
                }

                var appended = log.AppendFromLeader(request.Batch);
                return appended.Status == MembershipAppendStatus.Accepted
                    ? new MembershipAppendReceiveResult(
                        MembershipAppendReceiveStatus.Accepted,
                        election.CurrentTerm,
                        appended.MatchIndex,
                        appended.Status)
                    : new MembershipAppendReceiveResult(
                        MembershipAppendReceiveStatus.LogRejected,
                        election.CurrentTerm,
                        appended.MatchIndex,
                        appended.Status);
            }
        }

        private MembershipAppendReceiveResult Result(MembershipAppendReceiveStatus status)
        {
            return new MembershipAppendReceiveResult(
                status,
                election.CurrentTerm,
                log.LastIndex);
        }
    }
}
