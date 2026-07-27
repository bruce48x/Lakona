using System;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    public sealed class ClusterMembershipNodeOptions
    {
        public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(1);

        public TimeSpan ProofValidity { get; init; } = TimeSpan.FromSeconds(5);

        public TimeSpan MinimumRetryDelay { get; init; } = TimeSpan.FromMilliseconds(100);

        public TimeSpan MaximumRetryDelay { get; init; } = TimeSpan.FromSeconds(5);

        public TimeSpan JoinRetryWindow { get; init; } = TimeSpan.FromSeconds(30);

        internal void Validate()
        {
            if (HeartbeatInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(HeartbeatInterval));
            }

            if (ProofValidity <= HeartbeatInterval)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ProofValidity),
                    "Quorum proof validity must be greater than the heartbeat interval.");
            }

            if (MinimumRetryDelay <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(MinimumRetryDelay));
            }

            if (MaximumRetryDelay < MinimumRetryDelay)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaximumRetryDelay),
                    "Maximum retry delay cannot be less than the minimum retry delay.");
            }

            if (JoinRetryWindow < MinimumRetryDelay)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(JoinRetryWindow),
                    "Join retry window cannot be less than the minimum retry delay.");
            }
        }
    }
}
