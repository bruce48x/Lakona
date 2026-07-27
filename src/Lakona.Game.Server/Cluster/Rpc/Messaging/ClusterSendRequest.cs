using System;
using System.Collections.Generic;
using MemoryPack;

namespace Lakona.Game.Cluster.Rpc
{
    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class ClusterSendRequest
    {
        [MemoryPackOrder(0)]
        public string Route { get; set; } = string.Empty;

        [MemoryPackOrder(1)]
        public string Kind { get; set; } = string.Empty;

        [MemoryPackOrder(2)]
        public byte[] Payload { get; set; } = Array.Empty<byte>();

        [MemoryPackOrder(3)]
        public DateTimeOffset ExpiresAt { get; set; }

        [MemoryPackOrder(4)]
        public string SourceNode { get; set; } = string.Empty;

        [MemoryPackOrder(5)]
        public string? CorrelationId { get; set; }

        [MemoryPackOrder(6)]
        public string? TraceId { get; set; }

        [MemoryPackOrder(7)]
        public string? OrderedBy { get; set; }

        [MemoryPackOrder(8)]
        public Dictionary<string, string>? Metadata { get; set; }

        [MemoryPackOrder(9)]
        public Guid? TargetClusterIncarnation { get; set; }

        [MemoryPackOrder(10)]
        public string? TargetNode { get; set; }

        [MemoryPackOrder(11)]
        public Guid? TargetNodeIncarnation { get; set; }

        [MemoryPackOrder(12)]
        public long? TargetMembershipView { get; set; }
    }
}
