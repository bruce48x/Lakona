using System;
using Lakona.Rpc.Core;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class ClusterClientFactoryOptions
    {
        public RpcKeepAliveOptions KeepAlive { get; set; } = RpcKeepAliveOptions.Disabled;

        public TimeSpan? ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);
    }
}
