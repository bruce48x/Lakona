using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    public interface INodeMessenger
    {
        ValueTask<ClusterSendStatus> SendAsync(
            RouteLocation target,
            ClusterMessage message,
            CancellationToken cancellationToken = default);
    }
}
