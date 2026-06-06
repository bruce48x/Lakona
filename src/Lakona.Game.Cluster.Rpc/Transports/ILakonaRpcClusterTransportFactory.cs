using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
using Lakona.Rpc.Core;

namespace Lakona.Game.Cluster.Rpc
{
    public interface IULinkRpcClusterTransportFactory
    {
        ValueTask<ITransport> ConnectAsync(
            RouteLocation target,
            ULinkRpcClusterEndpoint endpoint,
            CancellationToken cancellationToken = default);
    }
}
