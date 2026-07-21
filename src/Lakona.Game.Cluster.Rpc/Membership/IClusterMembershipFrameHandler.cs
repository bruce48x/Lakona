using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    public interface IClusterMembershipFrameHandler
    {
        ValueTask<ClusterMembershipTransportFrame> HandleAsync(
            ClusterMembershipTransportFrame request,
            CancellationToken cancellationToken = default);
    }
}
