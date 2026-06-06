using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    public interface IClusterNodeSender
    {
        ValueTask<ClusterSendStatus> SendAsync(
            NodeId nodeId,
            RouteKey route,
            ClusterMessage message,
            CancellationToken cancellationToken = default);
    }
}
