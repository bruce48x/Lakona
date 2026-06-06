using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
using Lakona.Rpc.Core;

namespace Lakona.Game.Cluster.Rpc
{
    public interface IULinkRpcClusterClientFactory
    {
        ValueTask<IRpcClient> GetClientAsync(
            RouteLocation target,
            CancellationToken cancellationToken = default);
    }
}
