using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    public interface IClusterNodeDiscovery
    {
        ValueTask<IReadOnlyList<ClusterNodeDescriptor>> QueryAsync(
            ClusterNodeDiscoveryQuery query,
            CancellationToken cancellationToken = default);

        ValueTask<IReadOnlyList<ClusterNodeDescriptor>> ListAsync(
            IReadOnlyDictionary<string, string> labels,
            CancellationToken cancellationToken = default);

        ValueTask<ClusterNodeDescriptor?> AnyAsync(
            IReadOnlyDictionary<string, string> labels,
            CancellationToken cancellationToken = default);
    }
}
