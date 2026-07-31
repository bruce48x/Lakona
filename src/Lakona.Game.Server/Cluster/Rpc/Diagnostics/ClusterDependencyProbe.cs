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
