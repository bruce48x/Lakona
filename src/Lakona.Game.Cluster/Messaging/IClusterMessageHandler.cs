using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    public interface IClusterMessageHandler
    {
        ValueTask<ClusterSendStatus> HandleAsync(
            ClusterMessage message,
            CancellationToken cancellationToken = default);
    }
}
