using System;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    internal sealed class MembershipControlLoopOptions
    {
        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromMilliseconds(500);

        public TimeSpan MinimumRetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);

        public TimeSpan MaximumRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

        public void Validate()
        {
            if (HeartbeatInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(HeartbeatInterval),
                    "Heartbeat interval must be positive.");
            }

            if (MinimumRetryDelay <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MinimumRetryDelay),
                    "Minimum retry delay must be positive.");
            }

            if (MaximumRetryDelay < MinimumRetryDelay)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaximumRetryDelay),
                    "Maximum retry delay cannot be shorter than the minimum retry delay.");
            }
        }
    }
}
