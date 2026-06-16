using Lakona.Game.Cluster;
using Lakona.Game.Server.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lakona.Game.Server.Features;

public sealed class LakonaGameClusterRegistrationHostedService : IHostedService
{
    private const string ClusterName = "local";

    private readonly IServiceProvider _services;
    private readonly object _gate = new();
    private CancellationTokenSource? _heartbeatCts;
    private Task? _heartbeatTask;
    private INodeDirectory? _directory;
    private ClusterOptions? _options;
    private LakonaGameFeatureCatalog? _catalog;
    private NodeRecord? _record;

    public LakonaGameClusterRegistrationHostedService(IServiceProvider services)
    {
        _services = services;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var directory = _services.GetService<INodeDirectory>();
        var options = _services.GetService<ClusterOptions>();
        var catalog = _services.GetService<LakonaGameFeatureCatalog>();
        if (directory is null || options is null || catalog is null)
        {
            return;
        }

        _directory = directory;
        _options = options;
        _catalog = catalog;
        _record = await RegisterAsync(directory, options, catalog, cancellationToken)
            .ConfigureAwait(false);

        var heartbeatInterval = ResolveHeartbeatInterval(options);
        var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_gate)
        {
            _heartbeatCts = heartbeatCts;
            _heartbeatTask = RunHeartbeatLoopAsync(heartbeatInterval, heartbeatCts.Token);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? heartbeatCts;
        Task? heartbeatTask;
        lock (_gate)
        {
            heartbeatCts = _heartbeatCts;
            heartbeatTask = _heartbeatTask;
            _heartbeatCts = null;
            _heartbeatTask = null;
        }

        if (heartbeatCts is not null)
        {
            await heartbeatCts.CancelAsync().ConfigureAwait(false);
            heartbeatCts.Dispose();
        }

        if (heartbeatTask is not null)
        {
            try
            {
                await heartbeatTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        var record = _record;
        if (_directory is null || record is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        await _directory.UpdateStateAsync(
            record.ClusterName,
            record.NodeId,
            record.NodeEpoch,
            NodeState.Dead,
            now,
            cancellationToken).ConfigureAwait(false);

        if (_services.GetService<IRouteDirectory>() is IRouteDirectory routes)
        {
            await routes.ClearByNodeEpochAsync(record.NodeId, record.NodeEpoch, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task RunHeartbeatLoopAsync(
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await HeartbeatAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HeartbeatAsync(CancellationToken cancellationToken)
    {
        var directory = _directory;
        var options = _options;
        var catalog = _catalog;
        var record = _record;
        if (directory is null || options is null || catalog is null || record is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var leaseExpiresAt = now.AddSeconds(options.RouteLeaseSeconds);
        var status = await directory.HeartbeatAsync(
            record.ClusterName,
            record.NodeId,
            record.NodeEpoch,
            leaseExpiresAt,
            now,
            cancellationToken).ConfigureAwait(false);

        if (status == NodeHeartbeatStatus.Refreshed)
        {
            _record = new NodeRecord(
                record.ClusterName,
                record.NodeId,
                record.NodeEpoch,
                record.Endpoints,
                record.Features,
                record.Labels,
                record.State,
                leaseExpiresAt,
                now);
            return;
        }

        if (status is NodeHeartbeatStatus.NodeNotFound or NodeHeartbeatStatus.Expired)
        {
            _record = await RegisterAsync(directory, options, catalog, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (status == NodeHeartbeatStatus.EpochMismatch)
        {
            lock (_gate)
            {
                _heartbeatCts?.Cancel();
            }
        }
    }

    private async Task<NodeRecord> RegisterAsync(
        INodeDirectory directory,
        ClusterOptions options,
        LakonaGameFeatureCatalog catalog,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var registration = new NodeRegistration(
            ClusterName,
            new NodeId(options.NodeId),
            CreateEndpoints(options.AdvertisedEndpoints),
            CreateFeatures(catalog),
            now.AddSeconds(options.RouteLeaseSeconds),
            NodeState.Ready);
        var result = await directory.RegisterAsync(registration, now, cancellationToken)
            .ConfigureAwait(false);
        if (result.Status != NodeRegistrationStatus.Registered || result.Record is null)
        {
            throw new InvalidOperationException(
                $"Lakona.Game cluster node registration failed with status '{result.Status}'.");
        }

        return result.Record;
    }

    private TimeSpan ResolveHeartbeatInterval(ClusterOptions options)
    {
        var configured = _services.GetService<LakonaGameClusterRegistrationOptions>()?.HeartbeatInterval;
        if (configured is not null && configured.Value > TimeSpan.Zero)
        {
            return configured.Value;
        }

        var leaseSeconds = Math.Max(1, options.RouteLeaseSeconds);
        var interval = TimeSpan.FromSeconds(Math.Max(1, leaseSeconds / 3));
        return interval;
    }

    private static IReadOnlyDictionary<string, NodeEndpoint> CreateEndpoints(
        IReadOnlyDictionary<string, string> endpoints)
    {
        var result = new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal);
        foreach (var endpoint in endpoints)
        {
            result[endpoint.Key] = new NodeEndpoint(endpoint.Value);
        }

        return result;
    }

    private static IReadOnlyList<NodeFeatureDescriptor> CreateFeatures(
        LakonaGameFeatureCatalog catalog)
    {
        var result = new List<NodeFeatureDescriptor>();
        for (var i = 0; i < catalog.ActiveDefinitions.Count; i++)
        {
            var feature = i < catalog.ActiveFeatures.Count
                ? catalog.ActiveFeatures[i]
                : null;
            if (feature?.Discoverable == false)
            {
                continue;
            }

            result.Add(new NodeFeatureDescriptor(
                catalog.ActiveDefinitions[i].Name,
                feature?.Metadata));
        }

        return result;
    }
}
