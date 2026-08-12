using Lakona.Game.Cluster;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Actors;

internal sealed class ActorLocationCoordinator(
    IClusterMembership membership,
    ActorLocationDirectory directory,
    ILogger<ActorLocationCoordinator>? logger = null) : BackgroundService
{
    private const int MaximumConcurrentShardRecoveries = 8;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ClusterMembershipSnapshot snapshot;
        while (true)
        {
            try
            {
                snapshot = membership.Current;
                break;
            }
            catch (InvalidOperationException) when (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), stoppingToken).ConfigureAwait(false);
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await directory.StabilizeAsync(
                    snapshot,
                    MaximumConcurrentShardRecoveries,
                    stoppingToken).ConfigureAwait(false);
                snapshot = await membership.WaitForChangeAsync(snapshot.View, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger?.LogWarning(
                    exception,
                    "Actor Location could not stabilize Membership view {MembershipView}; shards remain unavailable until recovery succeeds.",
                    snapshot.View.Value);
                await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken).ConfigureAwait(false);
                snapshot = membership.Current;
            }
        }
    }
}
