using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    public sealed class ClusterRouter : IClusterRouter
    {
        private readonly NodeId _localNode;
        private readonly IRouteDirectory _routeDirectory;
        private readonly IClusterMessageHandler _localHandler;
        private readonly INodeMessenger _nodeMessenger;
        private readonly Func<DateTimeOffset> _utcNow;

        public ClusterRouter(
            NodeId localNode,
            IRouteDirectory routeDirectory,
            IClusterMessageHandler localHandler,
            INodeMessenger nodeMessenger,
            Func<DateTimeOffset>? utcNow = null)
        {
            _localNode = localNode;
            _routeDirectory = routeDirectory ?? throw new ArgumentNullException(nameof(routeDirectory));
            _localHandler = localHandler ?? throw new ArgumentNullException(nameof(localHandler));
            _nodeMessenger = nodeMessenger ?? throw new ArgumentNullException(nameof(nodeMessenger));
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        }

        public async ValueTask<ClusterSendStatus> SendAsync(
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            cancellationToken.ThrowIfCancellationRequested();

            using var activity = ClusterDiagnostics.StartActivity("send", message);
            var now = _utcNow();
            if (message.IsExpired(now))
            {
                activity?.SetTag("lakona-game.cluster.status", "expired");
                ClusterDiagnostics.AddExpired(message.Kind);
                ClusterDiagnostics.AddDrop("expired", message.Kind);
                ClusterDiagnostics.AddSend("expired", "none", message.Kind);
                return ClusterSendStatus.Expired;
            }

            var location = await _routeDirectory.ResolveAsync(
                message.Route,
                now,
                cancellationToken).ConfigureAwait(false);

            if (location is null)
            {
                activity?.SetTag("lakona-game.cluster.status", "route_not_found");
                ClusterDiagnostics.AddRouteLookup("route_not_found", message.Kind);
                ClusterDiagnostics.AddDrop("route_not_found", message.Kind);
                ClusterDiagnostics.AddSend("route_not_found", "none", message.Kind);
                return ClusterSendStatus.RouteNotFound;
            }

            ClusterDiagnostics.AddRouteLookup("found", message.Kind);
            if (location.Node == _localNode)
            {
                activity?.SetTag("lakona-game.cluster.delivery", "local");
                var localStatus = await _localHandler.HandleAsync(message, cancellationToken).ConfigureAwait(false);
                RecordCompletion(activity, localStatus, "local", message.Kind);
                ClusterDiagnostics.AddDispatch(ClusterDiagnostics.StatusTag(localStatus), "local", message.Kind);
                return localStatus;
            }

            activity?.SetTag("lakona-game.cluster.delivery", "remote");
            var remoteStatus = await _nodeMessenger.SendAsync(
                location,
                message,
                cancellationToken).ConfigureAwait(false);
            RecordCompletion(activity, remoteStatus, "remote", message.Kind);
            return remoteStatus;
        }

        private static void RecordCompletion(
            System.Diagnostics.Activity? activity,
            ClusterSendStatus status,
            string delivery,
            string kind)
        {
            var statusTag = ClusterDiagnostics.StatusTag(status);
            activity?.SetTag("lakona-game.cluster.status", statusTag);
            ClusterDiagnostics.AddSend(statusTag, delivery, kind);

            if (status == ClusterSendStatus.Backpressure)
            {
                ClusterDiagnostics.AddBackpressure("send", delivery, kind);
            }
        }
    }
}
