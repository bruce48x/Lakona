using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    /// <summary>
    /// Exchanges bounded membership protocol frames through the internal cluster channel.
    /// </summary>
    internal interface IClusterMembershipTransport
    {
        ValueTask<ClusterMembershipTransportFrame> RequestAsync(
            NodeEndpoint endpoint,
            ClusterMembershipTransportFrame request,
            CancellationToken cancellationToken = default);
    }
}
