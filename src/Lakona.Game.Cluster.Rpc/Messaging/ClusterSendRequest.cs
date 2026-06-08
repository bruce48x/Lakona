using System;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class ClusterSendRequest
    {
        public string Route { get; set; } = string.Empty;

        public string Kind { get; set; } = string.Empty;

        public byte[] Payload { get; set; } = Array.Empty<byte>();

        public DateTimeOffset ExpiresAt { get; set; }

        public string SourceNode { get; set; } = string.Empty;

        public string? CorrelationId { get; set; }

        public string? TraceId { get; set; }

        public string? OrderedBy { get; set; }
    }
}
