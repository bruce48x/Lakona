using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class ULinkRpcClusterDependencyProbe
    {
        private static readonly RouteKey HealthRoute = new RouteKey("__lakona-game/health__");

        private readonly IULinkRpcClusterClientFactory? _clientFactory;
        private readonly INodeDirectory? _nodeDirectory;
        private readonly string? _clusterName;
        private readonly NodeId _node;
        private readonly TimeSpan _timeout;

        public ULinkRpcClusterDependencyProbe(
            IULinkRpcClusterClientFactory clientFactory,
            TimeSpan? timeout = null)
        {
            _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
            _timeout = timeout ?? TimeSpan.FromSeconds(2);
            if (_timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "Health probe timeout must be positive.");
            }
        }

        private ULinkRpcClusterDependencyProbe(
            INodeDirectory nodeDirectory,
            string clusterName,
            NodeId node,
            TimeSpan? timeout)
        {
            _nodeDirectory = nodeDirectory ?? throw new ArgumentNullException(nameof(nodeDirectory));
            if (string.IsNullOrWhiteSpace(clusterName))
            {
                throw new ArgumentException("Cluster name is required.", nameof(clusterName));
            }

            _clusterName = clusterName;
            _node = node;
            _timeout = timeout ?? TimeSpan.FromSeconds(2);
            if (_timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "Health probe timeout must be positive.");
            }
        }

        public static ULinkRpcClusterDependencyProbe ForNodeDirectory(
            INodeDirectory directory,
            string clusterName,
            NodeId node,
            TimeSpan? timeout = null)
        {
            return new ULinkRpcClusterDependencyProbe(directory, clusterName, node, timeout);
        }

        public async ValueTask<ULinkRpcClusterDependencyHealth> CheckAsync(
            CancellationToken cancellationToken = default)
        {
            if (_nodeDirectory is null || _clusterName is null)
            {
                throw new InvalidOperationException("This dependency probe is not configured for node-directory checks.");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);

            try
            {
                await _nodeDirectory.ResolveAsync(_clusterName, _node, DateTimeOffset.UtcNow, timeout.Token)
                    .ConfigureAwait(false);
                return new ULinkRpcClusterDependencyHealth(
                    "node-directory",
                    ULinkRpcClusterDependencyStatus.Healthy);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new ULinkRpcClusterDependencyHealth(
                    "node-directory",
                    ULinkRpcClusterDependencyStatus.Timeout,
                    "Node directory health probe timed out.");
            }
            catch (Exception ex)
            {
                return new ULinkRpcClusterDependencyHealth(
                    "node-directory",
                    ULinkRpcClusterDependencyStatus.Unhealthy,
                    ex.Message);
            }
        }

        public async ValueTask<ULinkRpcClusterDependencyHealth> CheckRouteDirectoryAsync(
            RouteLocation routeDirectory,
            CancellationToken cancellationToken = default)
        {
            if (routeDirectory is null)
            {
                throw new ArgumentNullException(nameof(routeDirectory));
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);

            try
            {
                var clientFactory = _clientFactory ??
                    throw new InvalidOperationException("This dependency probe is not configured for route-directory checks.");
                var client = await clientFactory.GetClientAsync(routeDirectory, timeout.Token).ConfigureAwait(false);
                var directory = new ULinkRpcRouteDirectory(client);
                await directory.ResolveAsync(HealthRoute, DateTimeOffset.UtcNow, timeout.Token).ConfigureAwait(false);
                return new ULinkRpcClusterDependencyHealth(
                    "route-directory",
                    ULinkRpcClusterDependencyStatus.Healthy);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new ULinkRpcClusterDependencyHealth(
                    "route-directory",
                    ULinkRpcClusterDependencyStatus.Timeout,
                    "Route directory health probe timed out.");
            }
            catch (Exception ex)
            {
                return new ULinkRpcClusterDependencyHealth(
                    "route-directory",
                    ULinkRpcClusterDependencyStatus.Unhealthy,
                    ex.Message);
            }
        }
    }
}
