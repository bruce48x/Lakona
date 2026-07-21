using System;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    internal sealed class QuorumProof
    {
        public QuorumProof(
            ClusterIncarnationId cluster,
            long term,
            MembershipViewId view,
            long sequence,
            TimeSpan validFor)
        {
            if (cluster.Value == Guid.Empty)
            {
                throw new ArgumentException("Cluster incarnation id is required.", nameof(cluster));
            }

            if (term <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(term), "Consensus term must be positive.");
            }

            if (sequence <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sequence),
                    "Quorum proof sequence must be positive.");
            }

            if (validFor <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(validFor),
                    "Quorum proof validity must be positive.");
            }

            Cluster = cluster;
            Term = term;
            View = view;
            Sequence = sequence;
            ValidFor = validFor;
        }

        public ClusterIncarnationId Cluster { get; }

        public long Term { get; }

        public MembershipViewId View { get; }

        public long Sequence { get; }

        public TimeSpan ValidFor { get; }
    }
}
