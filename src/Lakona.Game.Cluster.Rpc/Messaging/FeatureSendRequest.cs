using System;
using MemoryPack;

namespace Lakona.Game.Cluster.Rpc
{
    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class FeatureSendRequest
    {
        [MemoryPackOrder(0)]
        public string Feature { get; set; } = string.Empty;

        [MemoryPackOrder(1)]
        public string Kind { get; set; } = string.Empty;

        [MemoryPackOrder(2)]
        public byte[] Payload { get; set; } = Array.Empty<byte>();

        [MemoryPackOrder(3)]
        public DateTimeOffset ExpiresAt { get; set; }

        [MemoryPackOrder(4)]
        public string SourceNode { get; set; } = string.Empty;

        [MemoryPackOrder(5)]
        public string CorrelationId { get; set; } = string.Empty;
    }
}
