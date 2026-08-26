using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Testing.Fixtures.App;
using Lakona.Game.Testing.Fixtures.Hotfix;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Testing.Tests;

public sealed class ActorClusterLifecycleTests
{
    [Fact]
    public async Task GracefulStopDrainsInFlightCallThenRelocatesActor()
    {
        await using var cluster = CreateCluster();
        await cluster.StartAsync(TestContext.Current.CancellationToken);

        var actors = cluster.Node("data-1").Services.GetRequiredService<ActorAccess>();
        var id = new CounterId("graceful");
        var initial = await actors.Place<CounterActor>(id)
            .EnsureAsync(TestContext.Current.CancellationToken);
        var control = cluster.Node("battle-1").Services.GetRequiredService<CounterControl>();
        var call = CounterCalls.WaitAndAddAsync(
            actors,
            id,
            TestContext.Current.CancellationToken).AsTask();
        await control.Entered.WaitAsync(TestContext.Current.CancellationToken);

        var stop = cluster.StopNodeAsync(
            "battle-1",
            TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal("battle-1", initial.Owner.Value);
        Assert.False(stop.IsCompleted);
        await Assert.ThrowsAnyAsync<ActorCallException>(async () =>
            await CounterCalls.AddAsync(
                actors,
                id,
                10,
                TestContext.Current.CancellationToken));

        control.Release();
        Assert.Equal(1, (await call).Value);
        await stop;
        await cluster.WaitForMembershipAsync(TestContext.Current.CancellationToken);

        var relocated = await actors.Place<CounterActor>(id)
            .EnsureAsync(TestContext.Current.CancellationToken);
        Assert.Equal("battle-2", relocated.Owner.Value);
        Assert.Equal(1, (await CounterCalls.AddAsync(
            actors,
            id,
            1,
            TestContext.Current.CancellationToken)).Value);

        await actors.Place<CounterActor>(id)
            .DestroyAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task KillFailsPendingCallAndRestartUsesNewIncarnation()
    {
        await using var cluster = CreateCluster(includeSecondBattle: false);
        await cluster.StartAsync(TestContext.Current.CancellationToken);

        var actors = cluster.Node("data-1").Services.GetRequiredService<ActorAccess>();
        var id = new CounterId("kill-restart");
        var oldNode = cluster.Node("battle-1");
        await actors.Place<CounterActor>(id)
            .EnsureAsync(TestContext.Current.CancellationToken);
        var control = oldNode.Services.GetRequiredService<CounterControl>();
        var call = CounterCalls.WaitAndAddAsync(
            actors,
            id,
            TestContext.Current.CancellationToken).AsTask();
        await control.Entered.WaitAsync(TestContext.Current.CancellationToken);

        await cluster.KillNodeAsync("battle-1", TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<ActorCallException>(async () => await call);

        var restarted = await cluster.StartNodeAsync(
            "battle-1",
            TestContext.Current.CancellationToken);
        await cluster.WaitForMembershipAsync(TestContext.Current.CancellationToken);
        var placement = await actors.Place<CounterActor>(id)
            .EnsureAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(oldNode.Reference, restarted.Reference);
        Assert.Equal("battle-1", placement.Owner.Value);
        Assert.Equal(2, (await CounterCalls.AddAsync(
            actors,
            id,
            2,
            TestContext.Current.CancellationToken)).Value);

        await actors.Place<CounterActor>(id)
            .DestroyAsync(TestContext.Current.CancellationToken);
    }

    private static LakonaTestCluster CreateCluster(bool includeSecondBattle = true)
    {
        var builder = new LakonaTestClusterBuilder()
            .AddNode("data-1", "data")
            .AddNode("battle-1", "battle");
        if (includeSecondBattle)
        {
            builder.AddNode("battle-2", "battle");
        }

        return builder
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
    }
}
