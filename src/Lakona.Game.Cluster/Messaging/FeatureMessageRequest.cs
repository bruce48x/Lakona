using System;

namespace Lakona.Game.Cluster
{
    public sealed class FeatureMessageRequest
    {
        public FeatureMessageRequest(
            FeatureName feature,
            string kind,
            ReadOnlyMemory<byte> payload,
            DateTimeOffset expiresAt,
            NodeId sourceNode,
            string correlationId)
        {
            if (string.IsNullOrWhiteSpace(feature.Value))
            {
                throw new ArgumentException("Feature name is required.", nameof(feature));
            }

            if (string.IsNullOrWhiteSpace(sourceNode.Value))
            {
                throw new ArgumentException("Source node is required.", nameof(sourceNode));
            }

            if (string.IsNullOrWhiteSpace(correlationId))
            {
                throw new ArgumentException("Correlation id is required.", nameof(correlationId));
            }

            Feature = feature;
            Kind = kind ?? string.Empty;
            Payload = payload.ToArray();
            ExpiresAt = expiresAt;
            SourceNode = sourceNode;
            CorrelationId = correlationId;
        }

        public FeatureName Feature { get; }

        public string Kind { get; }

        public ReadOnlyMemory<byte> Payload { get; }

        public DateTimeOffset ExpiresAt { get; }

        public NodeId SourceNode { get; }

        public string CorrelationId { get; }

        public bool IsExpired(DateTimeOffset now)
        {
            return now >= ExpiresAt;
        }
    }
}
