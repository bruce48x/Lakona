using System;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class ULinkRpcClusterNodeMessengerOptions
    {
        public TimeSpan? SendTimeout { get; set; } = TimeSpan.FromSeconds(5);

        public Func<Exception, ClusterSendStatus>? ExceptionMapper { get; set; }
    }
}
