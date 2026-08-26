using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Testing.Fixtures.App;
using Lakona.Game.Testing.Fixtures.Hotfix;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Lakona.Game.Testing.Tests;

public sealed class TestClusterReliabilityTests
{
    [Fact]
    public async Task ClustersWithTheSameNodeIdsKeepActorStateIsolated()
    {
        var results = await Task.WhenAll(
            RunIsolatedClusterAsync("first", 2),
            RunIsolatedClusterAsync("second", 7));

        Assert.Equal([2, 7], results.OrderBy(static value => value));
    }

    [Fact]
    public async Task DisposeDrainsAnActiveActorBeforeTheLastDirectoryOwnerStops()
    {
        var cluster = ActorCluster();
        await cluster.StartAsync(TestContext.Current.CancellationToken);
        var actors = cluster.Node("data-1").Services.GetRequiredService<ActorAccess>();
        await actors.Place<CounterActor>(new CounterId("dispose-active"))
            .EnsureAsync(TestContext.Current.CancellationToken);

        await cluster.DisposeAsync();

        Assert.DoesNotContain(cluster.Nodes, static node => node.IsActive);
    }

    [Fact]
    public async Task DisposeCleansOtherResourcesWhenOneHostedServiceFailsToStop()
    {
        var cluster = new LakonaTestClusterBuilder()
            .AddNode("data-1", "data")
            .ConfigureNodes(node => node.ConfigureServices((services, _) =>
            {
                services.AddSingleton<CleanupProbe>();
                services.AddSingleton<IHostedService, FailingStopHostedService>();
            }))
            .Build();
        await cluster.StartAsync(TestContext.Current.CancellationToken);
        var probe = cluster.Node("data-1").Services.GetRequiredService<CleanupProbe>();

        var failure = await Assert.ThrowsAsync<AggregateException>(async () =>
            await cluster.DisposeAsync());

        Assert.Contains("Expected stop failure", failure.ToString(), StringComparison.Ordinal);
        Assert.True(probe.Disposed);
        Assert.DoesNotContain(cluster.Nodes, static node => node.IsActive);
    }

    [Fact]
    public async Task StartupFailureKeepsBothOriginalAndCleanupErrors()
    {
        var cluster = new LakonaTestClusterBuilder()
            .AddNode("data-1", "data")
            .ConfigureNodes(node => node.ConfigureServices((services, _) =>
                services.AddSingleton<IHostedService, StartAndDisposeFailingService>()))
            .Build();

        var failure = await Assert.ThrowsAsync<AggregateException>(() =>
            cluster.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Expected startup failure", failure.ToString(), StringComparison.Ordinal);
        Assert.Contains("Expected cleanup failure", failure.ToString(), StringComparison.Ordinal);
        await cluster.DisposeAsync();
    }

    [Fact]
    public async Task ConvergenceTimeoutListsBlockedDirectedLinks()
    {
        await using var cluster = new LakonaTestClusterBuilder()
            .AddNode("data-1", "data")
            .Build();
        cluster.Network.BlockOneWay("data-1", "battle-1");

        var failure = await Assert.ThrowsAsync<TimeoutException>(() =>
            cluster.WaitForMembershipAsync(
                TimeSpan.FromMilliseconds(100),
                TestContext.Current.CancellationToken));

        Assert.Contains("data-1->battle-1", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FiveNodeClusterConvergesAcrossSeededKillAndRestartChurn()
    {
        await using var cluster = new LakonaTestClusterBuilder()
            .AddNode("node-1", "data")
            .AddNode("node-2", "data")
            .AddNode("node-3", "data")
            .AddNode("node-4", "data")
            .AddNode("node-5", "data")
            .Build();
        await cluster.StartAsync(TestContext.Current.CancellationToken);

        foreach (var nodeId in new[] { "node-3", "node-1", "node-5" })
        {
            var previous = cluster.Node(nodeId).Reference;
            await cluster.KillNodeAsync(nodeId, TestContext.Current.CancellationToken);
            var replacement = await cluster.StartNodeAsync(
                nodeId,
                TestContext.Current.CancellationToken);
            var snapshot = await cluster.WaitForMembershipAsync(
                TestContext.Current.CancellationToken);

            Assert.Equal(5, snapshot.Members.Count);
            Assert.NotEqual(previous, replacement.Reference);
            Assert.DoesNotContain(snapshot.Members, member => member.Reference == previous);
            Assert.Contains(snapshot.Members, member => member.Reference == replacement.Reference);
        }
    }

    private static async Task<int> RunIsolatedClusterAsync(string key, int delta)
    {
        await using var cluster = ActorCluster();
        await cluster.StartAsync(TestContext.Current.CancellationToken);
        var actors = cluster.Node("data-1").Services.GetRequiredService<ActorAccess>();
        var id = new CounterId(key);
        await actors.Place<CounterActor>(id)
            .EnsureAsync(TestContext.Current.CancellationToken);
        var value = (await CounterCalls.AddAsync(
            actors,
            id,
            delta,
            TestContext.Current.CancellationToken)).Value;
        await actors.Place<CounterActor>(id)
            .DestroyAsync(TestContext.Current.CancellationToken);
        return value;
    }

    private static LakonaTestCluster ActorCluster() =>
        new LakonaTestClusterBuilder()
            .AddNode("data-1", "data")
            .AddNode("gateway-1", "gateway")
            .AddNode("battle-1", "battle")
            .ConfigureNodes(node =>
            {
                node.UseHotfixAssembly(typeof(CounterBehavior).Assembly);
                if (node.Roles.Contains("battle", StringComparer.Ordinal))
                {
                    node.ConfigureServices(static (services, _) =>
                        services.AddSingleton<CounterControl>());
                }
            })
            .Build();

    private sealed class CleanupProbe : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingStopHostedService(CleanupProbe probe) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            GC.KeepAlive(probe);
            return Task.FromException(new InvalidOperationException("Expected stop failure."));
        }
    }

    private sealed class StartAndDisposeFailingService : IHostedService, IAsyncDisposable
    {
        public Task StartAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("Expected startup failure."));

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() =>
            ValueTask.FromException(new InvalidOperationException("Expected cleanup failure."));
    }
}
