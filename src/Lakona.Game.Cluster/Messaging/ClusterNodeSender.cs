using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    public sealed class ClusterNodeSender : IClusterNodeSender
    {
        private readonly INodeDirectory _nodeDirectory;
        private readonly INodeMessenger _nodeMessenger;
        private readonly ClusterNodeSenderOptions _options;

        public ClusterNodeSender(
            INodeDirectory nodeDirectory,
            INodeMessenger nodeMessenger,
            ClusterNodeSenderOptions? options = null)
        {
            _nodeDirectory = nodeDirectory ?? throw new ArgumentNullException(nameof(nodeDirectory));
            _nodeMessenger = nodeMessenger ?? throw new ArgumentNullException(nameof(nodeMessenger));
            _options = options ?? new ClusterNodeSenderOptions();
        }

        public async ValueTask<ClusterSendStatus> SendAsync(
            NodeId nodeId,
            RouteKey route,
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            _options.Validate();

            var now = DateTimeOffset.UtcNow;
            var record = await _nodeDirectory.ResolveAsync(
                _options.ClusterName,
                nodeId,
                now,
                cancellationToken).ConfigureAwait(false);

            if (record is null || record.IsExpired(now))
            {
                return ClusterSendStatus.Failed;
            }

            if (!record.Endpoints.TryGetValue(_options.EndpointName, out var endpoint))
            {
                return ClusterSendStatus.Failed;
            }

            var target = new RouteLocation(
                route,
                nodeId,
                endpoint,
                record.LeaseExpiresAt,
                record.NodeEpoch);

            return await _nodeMessenger.SendAsync(
                target,
                message,
                cancellationToken).ConfigureAwait(false);
        }
    }
}
