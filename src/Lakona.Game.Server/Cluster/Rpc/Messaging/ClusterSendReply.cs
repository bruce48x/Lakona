using MemoryPack;

namespace Lakona.Game.Cluster.Rpc
{
    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class ClusterSendReply
    {
        [MemoryPackOrder(0)]
        public int Status { get; set; }
    }
}
