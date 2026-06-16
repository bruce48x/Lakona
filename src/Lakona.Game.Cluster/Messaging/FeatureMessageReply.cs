using System;

namespace Lakona.Game.Cluster
{
    public sealed class FeatureMessageReply
    {
        public FeatureMessageReply(
            ClusterSendStatus status,
            ReadOnlyMemory<byte> payload,
            string? errorMessage = null)
        {
            Status = status;
            Payload = payload.ToArray();
            ErrorMessage = errorMessage;
        }

        public ClusterSendStatus Status { get; }

        public ReadOnlyMemory<byte> Payload { get; }

        public string? ErrorMessage { get; }
    }
}
