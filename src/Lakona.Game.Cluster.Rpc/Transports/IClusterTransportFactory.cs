using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
using Lakona.Rpc.Core;

namespace Lakona.Game.Cluster.Rpc
{
    public interface IClusterTransportFactory
    {
        ValueTask<ITransport> ConnectAsync(
            RouteLocation target,
            ClusterEndpoint endpoint,
            CancellationToken cancellationToken = default);
    }
}
