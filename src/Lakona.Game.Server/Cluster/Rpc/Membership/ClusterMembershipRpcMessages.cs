using MemoryPack;

namespace Lakona.Game.Cluster.Rpc
{
    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class ClusterMembershipFrameRequest
    {
        [MemoryPackOrder(0)]
        public byte[] Payload { get; set; } = System.Array.Empty<byte>();
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class ClusterMembershipFrameReply
    {
        [MemoryPackOrder(0)]
        public byte[] Payload { get; set; } = System.Array.Empty<byte>();
    }
}
