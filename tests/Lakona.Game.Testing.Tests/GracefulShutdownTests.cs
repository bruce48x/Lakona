using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Membership;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Testing.Fixtures.App;
using Lakona.Game.Testing.Fixtures.Hotfix;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Testing.Tests;

public sealed class GracefulShutdownTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Last_node_stops_startup_and_used_ordinary_actors_before_components(bool ordinaryActor)
    {
        await using var cluster = CreateCluster(1);
        await cluster.StartAsync(TestContext.Current.CancellationToken);
        var node = cluster.Node("node-1");
        var state = node.Services.GetRequiredService<CompositionProbeState>();
        Assert.Single(node.Services.GetRequiredService<IActorRuntime>().GetActiveActorIds(typeof(ShutdownActor)));
        if (ordinaryActor)
        {
            var actors = node.Services.GetRequiredService<IHotfixRuntimeAccessor>().Current.Services.GetRequiredService<ActorAccess>();
            var id = new CounterId("shutdown");
            await actors.Place<CounterActor>(id).EnsureAsync(TestContext.Current.CancellationToken);
            Assert.Equal(3, (await CounterCalls.AddAsync(actors, id, 3, TestContext.Current.CancellationToken)).Value);
        }

        await state.FirstTimerTick.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await cluster.StopNodeAsync(node.NodeId, TestContext.Current.CancellationToken);
        await cluster.StopNodeAsync(node.NodeId, TestContext.Current.CancellationToken);

        AssertStopped(state, ordinaryActor);
        Assert.DoesNotContain(state.StopMembership!.Members, member => member.State == ClusterMemberState.Active);
        Assert.Contains(state.StopTable!.Entries, member => member.Reference == node.Reference && member.Status == MembershipTableStatus.Stopping);
        var ticks = Volatile.Read(ref state.TimerTicks);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(ticks, Volatile.Read(ref state.TimerTicks));
    }

    [Fact]
    public async Task Two_nodes_stop_in_order_and_replacement_survives_old_owner_shutdown()
    {
        await using var cluster = CreateCluster(2);
        await cluster.StartAsync(TestContext.Current.CancellationToken);
        var first = cluster.Node("node-1");
        var second = cluster.Node("node-2");
        var firstState = first.Services.GetRequiredService<CompositionProbeState>();
        var secondState = second.Services.GetRequiredService<CompositionProbeState>();
        var actors = second.Services.GetRequiredService<IHotfixRuntimeAccessor>().Current.Services.GetRequiredService<ActorAccess>();
        var id = new CounterId("relocate");
        Assert.Equal("node-1", (await actors.Place<CounterActor>(id).EnsureAsync(TestContext.Current.CancellationToken)).Owner.Value);
        await cluster.StopNodeAsync(first.NodeId, TestContext.Current.CancellationToken);
        await cluster.WaitForMembershipAsync(TestContext.Current.CancellationToken);
        AssertStopped(firstState, true);
        Assert.Equal("node-2", (await actors.Place<CounterActor>(id).EnsureAsync(TestContext.Current.CancellationToken)).Owner.Value);
        Assert.Equal(5, (await CounterCalls.AddAsync(actors, id, 5, TestContext.Current.CancellationToken)).Value);
        await cluster.StopNodeAsync(second.NodeId, TestContext.Current.CancellationToken);
        AssertStopped(secondState, true);
    }

    private static void AssertStopped(CompositionProbeState state, bool ordinaryActor)
    {
        Assert.Equal(ordinaryActor
            ? new[] { "timer-stop", "startup-stop", "actor-stop", "component-stop", "generation-dispose", "state-dispose" }
            : new[] { "timer-stop", "startup-stop", "component-stop", "generation-dispose", "state-dispose" }, state.LifecycleEvents);
        Assert.Equal(1, state.Stops);
        Assert.Equal(1, state.StateDisposals);
    }

    private static LakonaTestCluster CreateCluster(int nodes)
    {
        var builder = new LakonaTestClusterBuilder();
        for (var i = 1; i <= nodes; i++) builder.AddNode($"node-{i}", "battle", "shutdown");
        return builder.ConfigureNodes(node =>
        {
            node.UseHotfixAssembly(typeof(CounterBehavior).Assembly);
            node.ConfigureServices(static (services, _) => services.AddSingleton<CounterControl>());
        }).Build();
    }
}
