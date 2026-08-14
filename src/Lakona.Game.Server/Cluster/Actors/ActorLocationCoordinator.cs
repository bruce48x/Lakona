using Lakona.Game.Cluster;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Cluster.Actors;

internal sealed class ActorLocationCoordinator : BackgroundService
{
    private const int MaximumConcurrentShardRecoveries = 8;
    private static readonly TimeSpan DefaultStabilizationTimeout = TimeSpan.FromSeconds(5);
    private readonly IClusterMembership membership;
    private readonly IActorLocationStabilizer stabilizer;
    private readonly ILogger<ActorLocationCoordinator>? logger;
    private readonly TimeSpan stabilizationTimeout;

    public ActorLocationCoordinator(
        IClusterMembership membership,
        IActorLocationStabilizer stabilizer,
        ILogger<ActorLocationCoordinator>? logger = null)
        : this(membership, stabilizer, DefaultStabilizationTimeout, logger)
    {
    }

    internal ActorLocationCoordinator(
        IClusterMembership membership,
        IActorLocationStabilizer stabilizer,
        TimeSpan stabilizationTimeout,
        ILogger<ActorLocationCoordinator>? logger = null)
    {
        this.membership = membership ?? throw new ArgumentNullException(nameof(membership));
        this.stabilizer = stabilizer ?? throw new ArgumentNullException(nameof(stabilizer));
        if (stabilizationTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(stabilizationTimeout));
        this.stabilizationTimeout = stabilizationTimeout;
        this.logger = logger;
    }

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
                stabilizer.ObserveRecoveryView(snapshot);
                using var stabilization = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                using var membershipChange = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                stabilization.CancelAfter(stabilizationTimeout);
                var stabilizationTask = stabilizer.StabilizeAsync(
                    snapshot,
                    MaximumConcurrentShardRecoveries,
                    stabilization.Token).AsTask();
                var changeTask = membership.WaitForChangeAsync(snapshot.View, membershipChange.Token).AsTask();
                try
                {
                    var completed = await Task.WhenAny(stabilizationTask, changeTask).ConfigureAwait(false);
                    if (completed == changeTask)
                    {
                        snapshot = await changeTask.ConfigureAwait(false);
                        await stabilization.CancelAsync().ConfigureAwait(false);
                        await DrainSupersededAsync(stabilizationTask, stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    await stabilizationTask.ConfigureAwait(false);
                    snapshot = await changeTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    stabilization.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
                {
                    snapshot = membership.Current;
                    continue;
                }
                finally
                {
                    if (!changeTask.IsCompleted)
                    {
                        await membershipChange.CancelAsync().ConfigureAwait(false);
                        await DrainSupersededAsync(changeTask, stoppingToken).ConfigureAwait(false);
                    }
                }
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

    private static async Task DrainSupersededAsync(Task supersededTask, CancellationToken stoppingToken)
    {
        try
        {
            await supersededTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
        }
    }
}
