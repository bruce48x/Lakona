using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lakona.Game.Server.Hosting;

public sealed class LakonaGameClusterRegistrationHostedService : IHostedService, IClusterNodeRegistrationRefresher
{
    private const string ClusterName = "local";

    private readonly IServiceProvider _services;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _registrationGate = new(1, 1);
    private CancellationTokenSource? _heartbeatCts;
    private Task? _heartbeatTask;
    private INodeDirectory? _directory;
    private ClusterOptions? _options;
    private ActorHostDescriptorCatalog? _actorHostCatalog;
    private StartupActorDescriptorCatalog? _startupActorCatalog;
    private NodeRecord? _record;

    public LakonaGameClusterRegistrationHostedService(IServiceProvider services)
    {
        _services = services;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var directory = _services.GetService<INodeDirectory>();
        var options = _services.GetService<ClusterOptions>();
        var actorHostCatalog = _services.GetService<ActorHostDescriptorCatalog>();
        if (directory is null || options is null)
        {
            return;
        }

        _directory = directory;
        _options = options;
        _actorHostCatalog = actorHostCatalog;
        _startupActorCatalog = _services.GetService<StartupActorDescriptorCatalog>();
        _record = await RegisterSerializedAsync(directory, options, cancellationToken).ConfigureAwait(false);

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
        await _registrationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
        var directory = _directory;
        var options = _options;
        var record = _record;
        if (directory is null || options is null || record is null)
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
                record.ActorHosts,
                record.StartupActors,
                record.Labels,
                record.State,
                leaseExpiresAt,
                now);
            return;
        }

        if (status is NodeHeartbeatStatus.NodeNotFound or NodeHeartbeatStatus.Expired)
        {
            _record = await RegisterAsync(directory, options, cancellationToken)
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
        finally
        {
            _registrationGate.Release();
        }
    }

    private async Task<NodeRecord> RegisterAsync(
        INodeDirectory directory,
        ClusterOptions options,
        CancellationToken cancellationToken,
        HotfixSnapshot? hotfixSnapshot = null)
    {
        var now = DateTimeOffset.UtcNow;
        var registration = new NodeRegistration(
            ClusterName,
            new NodeId(options.NodeId),
            CreateEndpoints(options.AdvertisedEndpoints),
            CreateActorHosts(_services.GetService<LakonaGameRuntimeOptions>(), _actorHostCatalog, hotfixSnapshot ?? _services.GetService<IHotfixManager>()?.Current),
            _startupActorCatalog?.Snapshot() ?? [],
            now.AddSeconds(options.RouteLeaseSeconds),
            NodeState.Ready,
            CreateLabels());
        var result = await directory.RegisterAsync(registration, now, cancellationToken)
            .ConfigureAwait(false);
        if (result.Status != NodeRegistrationStatus.Registered || result.Record is null)
        {
            throw new InvalidOperationException(
                $"Lakona.Game cluster node registration failed with status '{result.Status}'.");
        }

        return result.Record;
    }

    public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = _directory;
        var options = _options;
        if (directory is null || options is null)
        {
            return;
        }

        await _registrationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _record = await RegisterAsync(directory, options, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _registrationGate.Release();
        }
    }

    private async Task<NodeRecord> RegisterSerializedAsync(
        INodeDirectory directory,
        ClusterOptions options,
        CancellationToken cancellationToken)
    {
        await _registrationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RegisterAsync(directory, options, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _registrationGate.Release();
        }
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

    private static IReadOnlyList<NodeActorHostDescriptor> CreateActorHosts(
        LakonaGameRuntimeOptions? runtimeOptions,
        ActorHostDescriptorCatalog? catalog,
        HotfixSnapshot? hotfixSnapshot)
    {
        var configured = runtimeOptions?.ActorHosts ?? [];
        if (configured.Count == 0)
        {
            return [];
        }

        if (catalog is null && (hotfixSnapshot?.ActorHosts.Count ?? 0) == 0)
        {
            throw new InvalidOperationException(
                "Lakona:ActorHosts is configured but no actor host descriptor catalog is registered.");
        }

        var hotfixCatalog = hotfixSnapshot?.ActorHosts.ToDictionary(
            static descriptor => descriptor.Actor,
            StringComparer.OrdinalIgnoreCase);
        var result = new List<NodeActorHostDescriptor>(configured.Count);
        foreach (var actor in configured)
        {
            if (catalog is not null && catalog.TryGet(actor, out var descriptor))
            {
                result.Add(new NodeActorHostDescriptor(
                    descriptor.Actor,
                    descriptor.PolicyHash,
                    descriptor.BuildTag,
                    descriptor.Metadata));
                continue;
            }

            if (hotfixCatalog is null || !hotfixCatalog.TryGetValue(actor, out var hotfixDescriptor))
            {
                throw new InvalidOperationException(
                    $"Lakona:ActorHosts contains unknown actor host '{actor}'.");
            }

            result.Add(new NodeActorHostDescriptor(
                hotfixDescriptor.Actor,
                hotfixDescriptor.PolicyHash,
                hotfixDescriptor.BuildTag,
                hotfixDescriptor.Metadata));
        }

        return result
            .OrderBy(static host => host.Actor, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> CreateLabels() =>
        new Dictionary<string, string>(StringComparer.Ordinal);
}
