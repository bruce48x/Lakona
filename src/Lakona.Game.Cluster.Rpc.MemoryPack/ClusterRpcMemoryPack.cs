using MemoryPack;
using Lakona.Game.Cluster.Rpc.MemoryPack.Generated;
using Lakona.Rpc.Serializer.MemoryPack;

namespace Lakona.Game.Cluster.Rpc.MemoryPack
{
    public static class ClusterRpcMemoryPack
    {
        public static void RegisterFormatters() => GeneratedClusterRpcMemoryPackFormatters.Register();

        public static MemoryPackRpcSerializer CreateSerializer(MemoryPackSerializerOptions? options = null)
        {
            RegisterFormatters();
            return options is null ? new MemoryPackRpcSerializer() : new MemoryPackRpcSerializer(options);
        }
    }
}
