using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Testing.Fixtures.App;
using Lakona.Game.Testing.Fixtures.Hotfix;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Testing.Tests;

public sealed class ActorDirectoryResilienceTests
{
    [Fact]
    public async Task Actor_call_waits_for_a_lagging_receiver_membership_view_over_real_transport()
    {
        await using var cluster = CreateCluster("data-1", "battle-1");
        await cluster.StartAsync(TestContext.Current.CancellationToken);
        var actors = cluster.Node("data-1").Services.GetRequiredService<ActorAccess>();
        var id = new CounterId("membership-view-barrier");
        await actors.Place<CounterActor>(id)
            .EnsureAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, (await CounterCalls.AddAsync(
            actors,
            id,
            1,
            TestContext.Current.CancellationToken)).Value);

        var viewControl = cluster.MembershipViews;
        viewControl.Pause("battle-1");
        var resumed = false;
        try
        {
            var joining = cluster.StartAdditionalNodeAsync(
                "battle-2",
                ["battle"],
                TestContext.Current.CancellationToken);
            await viewControl.WaitUntilBehindAsync(
                "battle-1",
                TestContext.Current.CancellationToken);

            var receiverMembership = cluster.Node("battle-1").Services
                .GetRequiredService<IClusterMembership>();
            var senderMembership = cluster.Node("data-1").Services
                .GetRequiredService<IClusterMembership>();
            var heldView = receiverMembership.Current.View;
            while (senderMembership.Current.View.CompareTo(heldView) <= 0)
            {
                await senderMembership.WaitForChangeAsync(
                    senderMembership.Current.View,
                    TestContext.Current.CancellationToken);
            }

            var blockedBeforeCall = viewControl.GetBlockedWaiterCount("battle-1");
            var call = CounterCalls.AddAsync(
                actors,
                id,
                1,
                TestContext.Current.CancellationToken).AsTask();
            await viewControl.WaitForBlockedWaiterCountAsync(
                "battle-1",
                blockedBeforeCall + 1,
                TestContext.Current.CancellationToken);

            Assert.False(call.IsCompleted);
            viewControl.Resume("battle-1");
            resumed = true;

            Assert.Equal(2, (await call).Value);
            await joining;
            Assert.Equal(2, (await CounterCalls.AddAsync(
                actors,
                id,
                0,
                TestContext.Current.CancellationToken)).Value);
            await cluster.WaitForMembershipAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            if (!resumed)
            {
                viewControl.Resume("battle-1");
            }
        }

        await actors.Place<CounterActor>(id)
            .DestroyAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Node_join_converges_during_continuous_actor_create_call_and_destroy_load()
    {
        await using var cluster = CreateCluster("data-1", "battle-1");
        await cluster.StartAsync(TestContext.Current.CancellationToken);
        var actors = cluster.Node("data-1").Services.GetRequiredService<ActorAccess>();
        var loadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var load = Task.Run(async () =>
        {
            for (var index = 0; index < 80; index++)
            {
                var id = new CounterId($"joining-load-{index % 12:D2}");
                await actors.Place<CounterActor>(id)
                    .EnsureAsync(TestContext.Current.CancellationToken);
                Assert.True((await CounterCalls.AddAsync(
                    actors,
                    id,
                    1,
                    TestContext.Current.CancellationToken)).Value > 0);
                await actors.Place<CounterActor>(id)
                    .DestroyAsync(TestContext.Current.CancellationToken);
                loadStarted.TrySetResult();
                await Task.Delay(5, TestContext.Current.CancellationToken);
            }
        }, TestContext.Current.CancellationToken);
        await loadStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        await cluster.StartAdditionalNodeAsync(
            "battle-2",
            ["battle"],
            TestContext.Current.CancellationToken);
        await Task.WhenAll(
            load,
            cluster.WaitForMembershipAsync(TestContext.Current.CancellationToken));

        Assert.Equal(3, cluster.Nodes.Count(static node => node.IsActive));
    }

    [Fact]
    public async Task Seeded_five_node_actor_load_survives_stop_kill_and_restart_churn()
    {
        await using var cluster = CreateCluster(
            "data-1",
            "gateway-1",
            "battle-1",
            "battle-2",
            "battle-3");
        await cluster.StartAsync(TestContext.Current.CancellationToken);
        var ids = Enumerable.Range(0, 36)
            .Select(static index => new CounterId($"seeded-churn-{index:D2}"))
            .ToArray();
        var random = new Random(1977);

        await TouchInSeededOrderAsync(cluster, ids, random);
        await AssertDirectoryAndCatalogIntegrityAsync(cluster, ids);

        await cluster.KillNodeAsync("battle-1", TestContext.Current.CancellationToken);
        var restartedBattle1 = await cluster.StartNodeAsync(
            "battle-1",
            TestContext.Current.CancellationToken);
        await cluster.WaitForMembershipAsync(TestContext.Current.CancellationToken);
        await TouchInSeededOrderAsync(cluster, ids, random);
        await AssertDirectoryAndCatalogIntegrityAsync(cluster, ids);

        await cluster.StopNodeAsync("battle-2", TestContext.Current.CancellationToken);
        await cluster.WaitForMembershipAsync(TestContext.Current.CancellationToken);
        await TouchInSeededOrderAsync(cluster, ids, random);
        await AssertDirectoryAndCatalogIntegrityAsync(cluster, ids);

        var restartedBattle2 = await cluster.StartNodeAsync(
            "battle-2",
            TestContext.Current.CancellationToken);
        await cluster.WaitForMembershipAsync(TestContext.Current.CancellationToken);
        await AssertDirectoryAndCatalogIntegrityAsync(cluster, ids);

        Assert.True(restartedBattle1.IsActive);
        Assert.True(restartedBattle2.IsActive);
        await DestroyActorsAsync(cluster, ids);
    }

    [Fact]
    public async Task Partitioned_join_recovers_actor_directory_after_the_links_heal()
    {
        await using var cluster = CreateCluster("data-1", "battle-1");
        await cluster.StartAsync(TestContext.Current.CancellationToken);
        var ids = Enumerable.Range(0, 24)
            .Select(static index => new CounterId($"partitioned-join-{index:D2}"))
            .ToArray();
        await TouchInSeededOrderAsync(cluster, ids, new Random(48));

        cluster.Network.Partition("battle-2", "data-1");
        cluster.Network.Partition("battle-2", "battle-1");
        var joining = cluster.StartAdditionalNodeAsync(
            "battle-2",
            ["battle"],
            TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        cluster.Network.HealAll();

        await joining;
        await cluster.WaitForMembershipAsync(TestContext.Current.CancellationToken);
        await AssertDirectoryAndCatalogIntegrityAsync(cluster, ids);
        await DestroyActorsAsync(cluster, ids);
    }

    private static async Task TouchInSeededOrderAsync(
        LakonaTestCluster cluster,
        IReadOnlyList<CounterId> ids,
        Random random)
    {
        var callers = cluster.Nodes
            .Where(static node => node.IsActive)
            .Select(static node => node.Services.GetRequiredService<ActorAccess>())
            .ToArray();
        foreach (var id in ids.OrderBy(_ => random.Next()))
        {
            var actors = callers[random.Next(callers.Length)];
            await actors.Place<CounterActor>(id)
                .EnsureAsync(TestContext.Current.CancellationToken);
            await CounterCalls.AddAsync(
                actors,
                id,
                1,
                TestContext.Current.CancellationToken);
        }
    }

    private static async Task AssertDirectoryAndCatalogIntegrityAsync(
        LakonaTestCluster cluster,
        IReadOnlyList<CounterId> ids)
    {
        var activeNodes = cluster.Nodes.Where(static node => node.IsActive).ToArray();
        var actors = activeNodes[0].Services.GetRequiredService<ActorAccess>();
        var expectedByOwner = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            var placement = await actors.Place<CounterActor>(id)
                .EnsureAsync(TestContext.Current.CancellationToken);
            await CounterCalls.AddAsync(
                actors,
                id,
                0,
                TestContext.Current.CancellationToken);
            expectedByOwner[placement.Owner.Value] =
                expectedByOwner.GetValueOrDefault(placement.Owner.Value) + 1;
        }

        var activeIds = new List<ActorId>();
        foreach (var node in activeNodes)
        {
            var localIds = node.Services.GetRequiredService<IActorRuntime>()
                .GetActiveActorIds(typeof(CounterActor));
            activeIds.AddRange(localIds);
            Assert.Equal(expectedByOwner.GetValueOrDefault(node.NodeId), localIds.Count);
        }

        Assert.Equal(ids.Count, activeIds.Count);
        Assert.Equal(ids.Count, activeIds.Distinct().Count());
    }

    private static async Task DestroyActorsAsync(
        LakonaTestCluster cluster,
        IEnumerable<CounterId> ids)
    {
        var actors = cluster.Nodes.First(static node => node.IsActive)
            .Services.GetRequiredService<ActorAccess>();
        foreach (var id in ids)
        {
            await actors.Place<CounterActor>(id)
                .DestroyAsync(TestContext.Current.CancellationToken);
        }
    }

    private static LakonaTestCluster CreateCluster(params string[] nodeIds)
    {
        var builder = new LakonaTestClusterBuilder();
        foreach (var nodeId in nodeIds)
        {
            var role = nodeId.Split('-', 2)[0];
            builder.AddNode(nodeId, role);
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
