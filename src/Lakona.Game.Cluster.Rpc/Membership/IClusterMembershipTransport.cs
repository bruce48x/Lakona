using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    /// <summary>
    /// Exchanges bounded, transport-neutral membership protocol frames with a cluster endpoint.
    /// </summary>
    public interface IClusterMembershipTransport
    {
        ValueTask<ClusterMembershipTransportFrame> RequestAsync(
            NodeEndpoint endpoint,
            ClusterMembershipTransportFrame request,
            CancellationToken cancellationToken = default);
    }
}
