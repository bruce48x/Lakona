using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lakona.Game.Cluster
{
    public sealed class ClusterMessage
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

        public ClusterMessage(
            RouteKey route,
            string kind,
            ReadOnlyMemory<byte> payload,
            DateTimeOffset expiresAt,
            NodeId sourceNode,
            string? correlationId = null,
            string? traceId = null,
            string? orderedBy = null,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new ArgumentException("Message kind is required.", nameof(kind));
            }

            Route = route;
            Kind = kind;
            Payload = CopyPayload(payload);
            ExpiresAt = expiresAt;
            SourceNode = sourceNode;
            CorrelationId = correlationId;
            TraceId = traceId;
            OrderedBy = orderedBy;
            Metadata = metadata is null
                ? EmptyMetadata
                : new ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(metadata, StringComparer.Ordinal));
        }

        public RouteKey Route { get; }

        public string Kind { get; }

        public ReadOnlyMemory<byte> Payload { get; }

        public DateTimeOffset ExpiresAt { get; }

        public NodeId SourceNode { get; }

        public string? CorrelationId { get; }

        public string? TraceId { get; }

        public string? OrderedBy { get; }

        public IReadOnlyDictionary<string, string> Metadata { get; }

        public bool IsExpired(DateTimeOffset now)
        {
            return now >= ExpiresAt;
        }

        private static byte[] CopyPayload(ReadOnlyMemory<byte> payload)
        {
            return payload.ToArray();
        }
    }
}
