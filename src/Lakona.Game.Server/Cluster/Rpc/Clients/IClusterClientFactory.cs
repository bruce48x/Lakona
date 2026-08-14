using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
using Lakona.Rpc.Core;

namespace Lakona.Game.Cluster.Rpc
{
    public interface IClusterClientFactory
    {
        ValueTask<IRpcClient> GetClientAsync(
            RouteLocation target,
            CancellationToken cancellationToken = default);

        ValueTask<IRpcClient> GetClientAsync(
            NodeEndpoint contact,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "This cluster client factory does not support pre-membership contact endpoints.");
    }
}
