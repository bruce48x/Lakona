using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    public interface IClusterRouter
    {
        ValueTask<ClusterSendStatus> SendAsync(
            ClusterMessage message,
            CancellationToken cancellationToken = default);
    }
}
