using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Testing.Fixtures.App;
using Lakona.Game.Testing.Fixtures.Hotfix;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Testing.Tests;

public sealed class ActorClusterIntegrationTests
{
    [Fact]
    public async Task ActorPlacedFromOneNodeRunsOnItsRoleSelectedRemoteOwner()
    {
        await using var cluster = CreateCluster();
        await cluster.StartAsync(TestContext.Current.CancellationToken);

        var actors = cluster.Node("data-1").Services.GetRequiredService<ActorAccess>();
        var id = new CounterId("counter-1");
        var placement = await actors.Place<CounterActor>(id)
            .EnsureAsync(TestContext.Current.CancellationToken);
        var first = await CounterCalls.AddAsync(
            actors,
            id,
            2,
            TestContext.Current.CancellationToken);
        var second = await CounterCalls.AddAsync(
            actors,
            id,
            3,
            TestContext.Current.CancellationToken);

        Assert.Equal("battle-1", placement.Owner.Value);
        Assert.Equal(2, first.Value);
        Assert.Equal(5, second.Value);
        Assert.Empty(cluster.Node("data-1").Services
            .GetRequiredService<IActorRuntime>().GetActiveActorIds(typeof(CounterActor)));
        Assert.Single(cluster.Node("battle-1").Services
            .GetRequiredService<IActorRuntime>().GetActiveActorIds(typeof(CounterActor)));

        await actors.Place<CounterActor>(id)
            .DestroyAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ConcurrentCreateFromDifferentNodesProducesOneActivation()
    {
        await using var cluster = CreateCluster();
        await cluster.StartAsync(TestContext.Current.CancellationToken);

        var id = new CounterId("counter-race");
        var first = cluster.Node("data-1").Services.GetRequiredService<ActorAccess>();
        var second = cluster.Node("gateway-1").Services.GetRequiredService<ActorAccess>();
        var attempts = new[]
        {
            TryCreateAsync(first, id),
            TryCreateAsync(second, id)
        };

        var results = await Task.WhenAll(attempts);

        Assert.Single(results, static result => result);
        Assert.Single(cluster.Node("battle-1").Services
            .GetRequiredService<IActorRuntime>().GetActiveActorIds(typeof(CounterActor)));

        await first.Place<CounterActor>(id)
            .DestroyAsync(TestContext.Current.CancellationToken);
    }

    private static LakonaTestCluster CreateCluster() =>
        new LakonaTestClusterBuilder()
            .AddNode("data-1", "data")
            .AddNode("gateway-1", "gateway")
            .AddNode("battle-1", "battle")
            .ConfigureNodes(node =>
                node.UseHotfixAssembly(typeof(CounterBehavior).Assembly))
            .Build();

    private static async Task<bool> TryCreateAsync(ActorAccess actors, CounterId id)
    {
        try
        {
            await actors.Place<CounterActor>(id)
                .CreateAsync(TestContext.Current.CancellationToken);
            return true;
        }
        catch (ActorPlacementException)
        {
            return false;
        }
    }
}
