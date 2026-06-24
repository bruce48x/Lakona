using System;
using MemoryPack;

namespace Lakona.Game.Cluster.Rpc
{
    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class FeatureSendReply
    {
        [MemoryPackOrder(0)]
        public int Status { get; set; }

        [MemoryPackOrder(1)]
        public byte[] Payload { get; set; } = Array.Empty<byte>();

        [MemoryPackOrder(2)]
        public string? ErrorMessage { get; set; }
    }
}
