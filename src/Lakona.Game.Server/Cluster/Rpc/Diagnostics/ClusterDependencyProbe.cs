using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class ClusterDependencyProbe
    {
        private static readonly RouteKey HealthRoute = new RouteKey("__lakona-game/health__");

        private readonly IClusterClientFactory? _clientFactory;
        private readonly INodeDirectory? _nodeDirectory;
        private readonly string? _clusterName;
        private readonly NodeId _node;
        private readonly TimeSpan _timeout;

        public ClusterDependencyProbe(
            IClusterClientFactory clientFactory,
            TimeSpan? timeout = null)
        {
            _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
            _timeout = timeout ?? TimeSpan.FromSeconds(2);
            if (_timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "Health probe timeout must be positive.");
            }
        }

        private ClusterDependencyProbe(
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

        public static ClusterDependencyProbe ForNodeDirectory(
            INodeDirectory directory,
            string clusterName,
            NodeId node,
            TimeSpan? timeout = null)
        {
            return new ClusterDependencyProbe(directory, clusterName, node, timeout);
        }

        public async ValueTask<ClusterDependencyHealth> CheckAsync(
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
                return new ClusterDependencyHealth(
                    "node-directory",
                    ClusterDependencyStatus.Healthy);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new ClusterDependencyHealth(
                    "node-directory",
                    ClusterDependencyStatus.Timeout,
                    "Node directory health probe timed out.");
            }
            catch (Exception ex)
            {
                return new ClusterDependencyHealth(
                    "node-directory",
                    ClusterDependencyStatus.Unhealthy,
                    ex.Message);
            }
        }

        public async ValueTask<ClusterDependencyHealth> CheckRouteDirectoryAsync(
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
                var directory = new RouteDirectoryClient(client);
                await directory.ResolveAsync(HealthRoute, DateTimeOffset.UtcNow, timeout.Token).ConfigureAwait(false);
                return new ClusterDependencyHealth(
                    "route-directory",
                    ClusterDependencyStatus.Healthy);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new ClusterDependencyHealth(
                    "route-directory",
                    ClusterDependencyStatus.Timeout,
                    "Route directory health probe timed out.");
            }
            catch (Exception ex)
            {
                return new ClusterDependencyHealth(
                    "route-directory",
                    ClusterDependencyStatus.Unhealthy,
                    ex.Message);
            }
        }
    }
}
