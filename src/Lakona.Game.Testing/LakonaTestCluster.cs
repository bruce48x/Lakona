using Lakona.Game.Cluster;
using Lakona.Game.Server;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Lakona.Game.Testing;

/// <summary>Hosts and controls multiple Lakona server nodes in one test process.</summary>
public sealed class LakonaTestCluster : IAsyncDisposable
{
    private static readonly TimeSpan DefaultConvergenceTimeout = TimeSpan.FromSeconds(10);
    private readonly Dictionary<string, LakonaTestNodeSpecification> specifications;
    private readonly IReadOnlyList<Action<LakonaTestNodeBuilder>> configureNodes;
    private readonly Dictionary<string, LakonaTestNodeHandle> nodes =
        new(StringComparer.Ordinal);
    private readonly LakonaInProcessClusterInfrastructure clusterInfrastructure = new();
    private readonly InMemoryClusterTransportHub transportHub;
    private readonly SemaphoreSlim gate = new(1, 1);
    private int nextEndpointPort = 30000;
    private bool startAttempted;
    private bool disposed;

    internal LakonaTestCluster(
        Dictionary<string, LakonaTestNodeSpecification> specifications,
        IReadOnlyList<Action<LakonaTestNodeBuilder>> configureNodes)
    {
        this.specifications = specifications;
        this.configureNodes = configureNodes;
        Network = new LakonaTestNetwork();
        transportHub = new InMemoryClusterTransportHub(Network);
    }

    public IReadOnlyList<LakonaTestNodeHandle> Nodes
    {
        get
        {
            lock (nodes)
            {
                return nodes.Values
                    .OrderBy(static node => node.NodeId, StringComparer.Ordinal)
                    .ToArray();
            }
        }
    }

    public LakonaTestNetwork Network { get; }

    public LakonaTestNodeHandle Node(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        lock (nodes)
        {
            return nodes.TryGetValue(nodeId, out var node)
                ? node
                : throw new KeyNotFoundException(
                    $"Lakona TestCluster does not contain node '{nodeId}'.");
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (startAttempted)
            {
                throw new InvalidOperationException(
                    "Lakona TestCluster has already started.");
            }

            startAttempted = true;
            foreach (var specification in specifications.Values)
            {
                await StartNodeCoreAsync(specification, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception startFailure)
        {
            try
            {
                await StopAllCoreAsync(kill: false, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "Lakona TestCluster startup and rollback both failed.",
                    startFailure,
                    cleanupFailure);
            }

            throw;
        }
        finally
        {
            gate.Release();
        }

        await WaitForMembershipAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<LakonaTestNodeHandle> StartNodeAsync(
        string nodeId,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!specifications.TryGetValue(nodeId, out var specification))
            {
                throw new KeyNotFoundException(
                    $"Lakona TestCluster has no node specification for '{nodeId}'.");
            }

            return await StartNodeCoreAsync(specification, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<LakonaTestNodeHandle> StartAdditionalNodeAsync(
        string nodeId,
        IEnumerable<string>? roles = null,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (specifications.ContainsKey(nodeId))
            {
                throw new InvalidOperationException(
                    $"Lakona TestCluster already contains node '{nodeId}'.");
            }

            var specification = LakonaTestNodeSpecification.Create(nodeId, roles);
            var builder = new LakonaTestNodeBuilder(specification);
            foreach (var configure in configureNodes)
            {
                configure(builder);
            }

            specifications.Add(specification.NodeId, specification);
            return await StartNodeCoreAsync(specification, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<LakonaTestNodeHandle> StopNodeAsync(
        string nodeId,
        CancellationToken cancellationToken = default) =>
        StopNodeAsync(nodeId, kill: false, cancellationToken);

    public Task<LakonaTestNodeHandle> KillNodeAsync(
        string nodeId,
        CancellationToken cancellationToken = default) =>
        StopNodeAsync(nodeId, kill: true, cancellationToken);

    public async Task<LakonaTestNodeHandle> RestartNodeAsync(
        string nodeId,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!specifications.TryGetValue(nodeId, out var specification))
            {
                throw new KeyNotFoundException(
                    $"Lakona TestCluster has no node specification for '{nodeId}'.");
            }

            if (TryGetNode(nodeId, out var current) && current is { IsActive: true })
            {
                await StopNodeCoreAsync(current, kill: false, cancellationToken)
                    .ConfigureAwait(false);
            }

            return await StartNodeCoreAsync(specification, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ClusterMembershipSnapshot> WaitForMembershipAsync(
        CancellationToken cancellationToken = default) =>
        await WaitForMembershipAsync(DefaultConvergenceTimeout, cancellationToken)
            .ConfigureAwait(false);

    public async Task<ClusterMembershipSnapshot> WaitForMembershipAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeoutSource.CancelAfter(timeout);
        while (true)
        {
            timeoutSource.Token.ThrowIfCancellationRequested();
            var active = Nodes.Where(static node => node.IsActive).ToArray();
            if (active.Length > 0 && TryGetConvergedSnapshot(active, out var snapshot))
            {
                return snapshot;
            }

            try
            {
                await Task.Delay(20, timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested
                && timeoutSource.IsCancellationRequested)
            {
                throw new TimeoutException(CreateConvergenceFailure(active, timeout));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            List<Exception>? failures = null;
            try
            {
                await StopAllCoreAsync(kill: false, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }

            try
            {
                await transportHub.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }

            if (failures is not null)
            {
                throw new AggregateException(
                    "Lakona TestCluster disposal failed after attempting every cleanup step.",
                    failures);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<LakonaTestNodeHandle> StopNodeAsync(
        string nodeId,
        bool kill,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var node = Node(nodeId);
            await StopNodeCoreAsync(node, kill, cancellationToken)
                .ConfigureAwait(false);
            return node;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<LakonaTestNodeHandle> StartNodeCoreAsync(
        LakonaTestNodeSpecification specification,
        CancellationToken cancellationToken)
    {
        if (TryGetNode(specification.NodeId, out var current) && current is { IsActive: true })
        {
            throw new InvalidOperationException(
                $"Lakona TestCluster node '{specification.NodeId}' is already active.");
        }

        var endpointPort = current is null
            ? nextEndpointPort++
            : current.EndpointPort;
        var host = BuildHost(specification, endpointPort);
        try
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            var membership = host.Services.GetRequiredService<IClusterMembership>();
            var reference = membership.Current.Members
                .Single(member =>
                    member.Reference.Node.Value == specification.NodeId
                    && member.State == ClusterMemberState.Active)
                .Reference;
            var handle = new LakonaTestNodeHandle(
                specification,
                host,
                reference,
                endpointPort);
            lock (nodes)
            {
                nodes[specification.NodeId] = handle;
            }

            return handle;
        }
        catch (Exception startFailure)
        {
            try
            {
                await DisposeHostAsync(host).ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    $"Lakona TestCluster node '{specification.NodeId}' startup and cleanup both failed.",
                    startFailure,
                    cleanupFailure);
            }

            throw;
        }
    }

    private IHost BuildHost(
        LakonaTestNodeSpecification specification,
        int endpointPort)
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            EnvironmentName = Environments.Development
        });
        foreach (var configure in specification.ConfigurationActions)
        {
            configure(builder.Configuration);
        }

        var owned = CreateOwnedConfiguration(specification, endpointPort);
        builder.Configuration.AddInMemoryCollection(owned);
        builder.Services.AddLakonaGameServer(builder.Configuration);
        foreach (var configure in specification.ServiceActions)
        {
            configure(builder.Services, builder.Configuration);
        }

        builder.Services.TryAddSingleton<IHotfixRuntimeAccessor, TestHotfixRuntimeAccessor>();
        clusterInfrastructure.ConfigureNode(
            builder.Services,
            new InMemoryClusterRpcTransport(specification.NodeId, transportHub),
            specification.Roles,
            specification.HotfixAssembly);
        return builder.Build();
    }

    private static Dictionary<string, string?> CreateOwnedConfiguration(
        LakonaTestNodeSpecification specification,
        int endpointPort)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Lakona:Node:Id"] = specification.NodeId,
            ["Lakona:Cluster:Endpoint"] =
                $"{InMemoryClusterTransportHub.Scheme}://127.0.0.1:{endpointPort}",
            ["Lakona:Cluster:Membership:Provider"] =
                LakonaGameMembershipOptions.MemoryProvider,
            ["Lakona:Cluster:Membership:ProbeIntervalSeconds"] = "1",
            ["Lakona:Cluster:Membership:ProbeTimeoutSeconds"] = "1",
            ["Lakona:Cluster:Membership:FailedProbesBeforeSuspect"] = "2",
            ["Lakona:Cluster:Membership:VotesForDeath"] = "1",
            ["Lakona:Cluster:Membership:TableRefreshSeconds"] = "1",
            ["Lakona:Cluster:Membership:IAmAliveSeconds"] = "3",
            ["Lakona:Cluster:Membership:AllowedIAmAliveMissSeconds"] = "10",
            ["Lakona:Cluster:Membership:DefunctEntryRetentionSeconds"] = "60",
            ["Lakona:Cluster:Membership:DefunctEntryCleanupIntervalSeconds"] = "30"
        };
        for (var index = 0; index < specification.Roles.Count; index++)
        {
            values[$"Lakona:Node:Roles:{index}"] = specification.Roles[index];
        }

        return values;
    }

    private static bool TryGetConvergedSnapshot(
        IReadOnlyList<LakonaTestNodeHandle> active,
        out ClusterMembershipSnapshot snapshot)
    {
        snapshot = null!;
        var expected = active.Select(static node => node.Reference).ToHashSet();
        ClusterMembershipSnapshot? first = null;
        foreach (var node in active)
        {
            ClusterMembershipSnapshot current;
            try
            {
                current = node.Services.GetRequiredService<IClusterMembership>().Current;
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            var currentActive = current.Members
                .Where(static member => member.State == ClusterMemberState.Active)
                .Select(static member => member.Reference)
                .ToHashSet();
            if (!currentActive.SetEquals(expected))
            {
                return false;
            }

            if (first is not null
                && (current.Cluster != first.Cluster || current.View != first.View))
            {
                return false;
            }

            first ??= current;
        }

        snapshot = first!;
        return first is not null;
    }

    private string CreateConvergenceFailure(
        IReadOnlyList<LakonaTestNodeHandle> active,
        TimeSpan timeout)
    {
        var views = active.Select(node =>
        {
            try
            {
                var snapshot = node.Services.GetRequiredService<IClusterMembership>().Current;
                return $"{node.NodeId}=view:{snapshot.View.Value},members:[{string.Join(',', snapshot.Members.Select(member => $"{member.Reference.Node.Value}/{member.State}"))}]";
            }
            catch (Exception exception)
            {
                return $"{node.NodeId}=unavailable:{exception.Message}";
            }
        });
        var blockedLinks = Network.BlockedLinks.Count == 0
            ? "none"
            : string.Join(",", Network.BlockedLinks.Select(static link =>
                $"{link.SourceNodeId}->{link.TargetNodeId}"));
        var nodeViews = active.Count == 0 ? "no active nodes" : string.Join("; ", views);
        return $"Lakona TestCluster did not converge within {timeout}. {nodeViews}. Blocked links: {blockedLinks}.";
    }

    private bool TryGetNode(string nodeId, out LakonaTestNodeHandle? node)
    {
        lock (nodes)
        {
            return nodes.TryGetValue(nodeId, out node);
        }
    }

    private static async Task StopNodeCoreAsync(
        LakonaTestNodeHandle node,
        bool kill,
        CancellationToken cancellationToken)
    {
        if (!node.TryDeactivate())
        {
            return;
        }

        try
        {
            if (kill)
            {
                using var canceled = new CancellationTokenSource();
                canceled.Cancel();
                try
                {
                    await node.Host.StopAsync(canceled.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (AggregateException exception) when (ContainsOnlyCancellation(exception))
                {
                }
            }
            else
            {
                await node.Host.StopAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await DisposeHostAsync(node.Host).ConfigureAwait(false);
        }
    }

    private async Task StopAllCoreAsync(bool kill, CancellationToken cancellationToken)
    {
        List<Exception>? failures = null;
        var stopOrder = Nodes
            .Where(static node => node.IsActive)
            .OrderByDescending(HasActiveActors)
            .ThenByDescending(static node => node.NodeId, StringComparer.Ordinal)
            .ToArray();
        foreach (var node in stopOrder)
        {
            try
            {
                await StopNodeCoreAsync(node, kill, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }

            if (!kill && Nodes.Any(static candidate => candidate.IsActive))
            {
                try
                {
                    await WaitForMembershipAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }
        }

        if (failures is not null)
        {
            throw new AggregateException(
                "One or more Lakona TestCluster nodes failed to stop.",
                failures);
        }
    }

    private static bool HasActiveActors(LakonaTestNodeHandle node)
    {
        try
        {
            return node.Services.GetRequiredService<Lakona.Game.Server.Actors.IActorRuntime>()
                .GetDiagnosticsSnapshot()
                .ActorTypes
                .Any(static actorType => actorType.ActiveCount > 0);
        }
        catch
        {
            return false;
        }
    }

    private static async ValueTask DisposeHostAsync(IHost host)
    {
        if (host is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            host.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private static bool ContainsOnlyCancellation(AggregateException exception)
    {
        var flattened = exception.Flatten();
        return flattened.InnerExceptions.Count > 0
            && flattened.InnerExceptions.All(static inner =>
                inner is OperationCanceledException);
    }
}
