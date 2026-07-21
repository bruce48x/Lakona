using System;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    /// <summary>
    /// An opaque membership protocol frame. Applications can carry it over any request/reply transport.
    /// </summary>
    public sealed class ClusterMembershipTransportFrame
    {
        public const int MaximumPayloadLength = (5 * 1024 * 1024) + 4096;
        private readonly byte[] payload;

        public ClusterMembershipTransportFrame(ReadOnlyMemory<byte> payload)
        {
            if (payload.Length == 0 || payload.Length > MaximumPayloadLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(payload),
                    $"Membership frames must contain 1 to {MaximumPayloadLength} bytes.");
            }

            this.payload = payload.ToArray();
        }

        public ReadOnlyMemory<byte> Payload => payload;
    }
}
