using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Membership;
using Lakona.Game.Cluster.Tests.Testing;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class ClusterMembershipScenarioTests
{
    [Theory]
    [InlineData(104729)]
    [InlineData(130363)]
    [InlineData(155921)]
    [InlineData(196613)]
    [InlineData(262147)]
    public async Task Concurrent_join_and_repeated_restart_converge_to_one_live_incarnation_per_node(int seed)
    {
        var scenario = new DeterministicClusterScenario(seed);
        var table = new InMemoryMembershipTable();
        var nodeCount = scenario.Next(3, 11);
        var nodes = Enumerable.Range(0, nodeCount)
            .Select(index => CreateNode(table, index))
            .ToArray();
        scenario.Record($"join={nodeCount}");

        await Task.WhenAll(nodes.Select(node => JoinAndActivateAsync(node.Manager)));
        await RefreshAllAsync(nodes);
        scenario.AssertOneLiveIncarnationPerNode(await table.ReadOrCreateAsync(TestContext.Current.CancellationToken));
        scenario.AssertConverged(nodes.Select(static node => node.State).ToArray());

        var restartCount = scenario.Next(1, Math.Min(5, nodeCount) + 1);
        for (var restart = 0; restart < restartCount; restart++)
        {
            var index = scenario.Next(0, nodeCount);
            scenario.Record($"restart=node-{index}");
            nodes[index] = CreateNode(table, index);
            await JoinAndActivateAsync(nodes[index].Manager);
            await RefreshAllAsync(nodes);
            scenario.AssertOneLiveIncarnationPerNode(await table.ReadOrCreateAsync(TestContext.Current.CancellationToken));
            scenario.AssertConverged(nodes.Select(static node => node.State).ToArray());
        }
    }

    private static ScenarioNode CreateNode(IMembershipTable table, int index)
    {
        var state = new ClusterMembershipState();
        return new ScenarioNode(
            new MembershipTableManager(
                new NodeId($"node-{index}"),
                NodeIncarnationId.New(),
                new NodeEndpoint($"tcp://127.0.0.1:{22000 + index}"),
                new ClusterBuildTag("TestBuild1"),
                table,
                state),
            state);
    }

    private static async Task JoinAndActivateAsync(MembershipTableManager manager)
    {
        await manager.JoinAsync(TestContext.Current.CancellationToken);
        await manager.ActivateAsync(null, [], [], TestContext.Current.CancellationToken);
    }

    private static Task RefreshAllAsync(IEnumerable<ScenarioNode> nodes) =>
        Task.WhenAll(nodes.Select(node => node.Manager.RefreshAsync(TestContext.Current.CancellationToken).AsTask()));

    private sealed record ScenarioNode(MembershipTableManager Manager, ClusterMembershipState State);
}
