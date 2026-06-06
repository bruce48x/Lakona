using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    public sealed class InMemoryLoopbackNodeMessenger : INodeMessenger
    {
        private readonly object _gate = new object();
        private readonly Dictionary<NodeId, IClusterMessageHandler> _handlers =
            new Dictionary<NodeId, IClusterMessageHandler>();

        public void RegisterNode(NodeId node, IClusterMessageHandler handler)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            lock (_gate)
            {
                _handlers[node] = handler;
            }
        }

        public bool UnregisterNode(NodeId node)
        {
            lock (_gate)
            {
                return _handlers.Remove(node);
            }
        }

        public async ValueTask<ClusterSendStatus> SendAsync(
            RouteLocation target,
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            if (target is null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            cancellationToken.ThrowIfCancellationRequested();

            IClusterMessageHandler? handler;
            lock (_gate)
            {
                _handlers.TryGetValue(target.Node, out handler);
            }

            if (handler is null)
            {
                ClusterDiagnostics.AddReceive("handler_unavailable", message.Kind);
                ClusterDiagnostics.AddDrop("handler_unavailable", message.Kind);
                return ClusterSendStatus.HandlerUnavailable;
            }

            using var activity = ClusterDiagnostics.StartActivity("receive", message);
            activity?.SetTag("lakona-game.cluster.delivery", "remote");

            var status = await handler.HandleAsync(message, cancellationToken).ConfigureAwait(false);
            var statusTag = ClusterDiagnostics.StatusTag(status);
            activity?.SetTag("lakona-game.cluster.status", statusTag);

            ClusterDiagnostics.AddReceive(statusTag, message.Kind);
            if (status == ClusterSendStatus.Backpressure)
            {
                ClusterDiagnostics.AddBackpressure("receive", "remote", message.Kind);
            }

            return status;
        }
    }
}
