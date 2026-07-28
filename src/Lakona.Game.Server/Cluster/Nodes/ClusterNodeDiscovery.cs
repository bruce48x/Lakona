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
            IReadOnlyDictionary<string, string> labels,
            CancellationToken cancellationToken = default)
        {
            var records = await _nodeDirectory.QueryAsync(
                new NodeDirectoryQuery(
                    _options.ClusterName,
                    state: NodeState.Ready,
                    labels: labels),
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);

            return records
                .Select(ClusterNodeDescriptor.FromRecord)
                .ToArray();
        }

        public async ValueTask<ClusterNodeDescriptor?> AnyAsync(
            IReadOnlyDictionary<string, string> labels,
            CancellationToken cancellationToken = default)
        {
            var nodes = await ListAsync(labels, cancellationToken).ConfigureAwait(false);
            return nodes.Count == 0 ? null : nodes[0];
        }
    }
}
