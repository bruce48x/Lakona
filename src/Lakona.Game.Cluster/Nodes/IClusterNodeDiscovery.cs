using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    public interface IClusterNodeDiscovery
    {
        ValueTask<IReadOnlyList<ClusterNodeDescriptor>> ListAsync(
            FeatureName feature,
            CancellationToken cancellationToken = default);

        ValueTask<ClusterNodeDescriptor?> AnyAsync(
            FeatureName feature,
            CancellationToken cancellationToken = default);
    }
}
