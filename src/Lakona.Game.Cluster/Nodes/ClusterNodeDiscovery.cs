using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    public sealed class ClusterNodeDiscovery : IClusterNodeDiscovery
    {
        private readonly INodeDirectory _nodeDirectory;
        private readonly ClusterNodeDiscoveryOptions _options;

        public ClusterNodeDiscovery(
            INodeDirectory nodeDirectory,
            ClusterNodeDiscoveryOptions? options = null)
        {
            _nodeDirectory = nodeDirectory ?? throw new ArgumentNullException(nameof(nodeDirectory));
            _options = options ?? new ClusterNodeDiscoveryOptions();
            _options.Validate();
        }

        public async ValueTask<IReadOnlyList<ClusterNodeDescriptor>> ListAsync(
            ClusterFeature feature,
            CancellationToken cancellationToken = default)
        {
            var records = await _nodeDirectory.QueryAsync(
                new NodeDirectoryQuery(
                    _options.ClusterName,
                    serviceKind: feature.Value,
                    state: NodeState.Ready),
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);

            return records
                .Select(ClusterNodeDescriptor.FromRecord)
                .ToArray();
        }

        public async ValueTask<NodeId?> AnyAsync(
            ClusterFeature feature,
            CancellationToken cancellationToken = default)
        {
            var nodes = await ListAsync(feature, cancellationToken).ConfigureAwait(false);
            return nodes.Count == 0 ? (NodeId?)null : nodes[0].Node;
        }
    }
}
