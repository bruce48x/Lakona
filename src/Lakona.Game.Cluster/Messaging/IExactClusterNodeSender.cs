using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    public interface IExactClusterNodeSender
    {
        ValueTask<ClusterSendStatus> SendAsync(
            NodeReference target,
            MembershipViewId view,
            RouteKey route,
            ClusterMessage message,
            CancellationToken cancellationToken = default);
    }
}
