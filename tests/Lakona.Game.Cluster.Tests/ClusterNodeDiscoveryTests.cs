using Lakona.Game.Cluster;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class ClusterNodeDiscoveryTests
{
    [Fact]
    public async Task ListAsync_returns_ready_nodes_that_provide_feature()
    {
        var now = DateTimeOffset.UtcNow;
        var directory = new InMemoryNodeDirectory();
        await RegisterAsync(directory, "node-room-a", "room", NodeState.Ready, now);
        await RegisterAsync(directory, "node-room-b", "room", NodeState.Ready, now);
        await RegisterAsync(directory, "node-chat", "chat", NodeState.Ready, now);
        await RegisterAsync(directory, "node-starting", "room", NodeState.Starting, now);
        var discovery = new ClusterNodeDiscovery(directory);

        var nodes = await discovery.ListAsync(new ClusterFeature("room"), TestContext.Current.CancellationToken);

        Assert.Collection(
            nodes,
            node =>
            {
                Assert.Equal(new NodeId("node-room-a"), node.Node);
                Assert.Equal(NodeState.Ready, node.State);
                Assert.Contains(node.Services, service => service.Kind == "room");
            },
            node =>
            {
                Assert.Equal(new NodeId("node-room-b"), node.Node);
                Assert.Equal(NodeState.Ready, node.State);
                Assert.Contains(node.Services, service => service.Kind == "room");
            });
    }

    [Fact]
    public async Task AnyAsync_returns_first_ready_node_for_feature()
    {
        var now = DateTimeOffset.UtcNow;
        var directory = new InMemoryNodeDirectory();
        await RegisterAsync(directory, "node-b", "room", NodeState.Ready, now);
        await RegisterAsync(directory, "node-a", "room", NodeState.Ready, now);
        var discovery = new ClusterNodeDiscovery(directory);

        var node = await discovery.AnyAsync(new ClusterFeature("room"), TestContext.Current.CancellationToken);

        Assert.Equal(new NodeId("node-a"), node);
    }

    [Fact]
    public async Task AnyAsync_returns_null_when_feature_is_missing()
    {
        var now = DateTimeOffset.UtcNow;
        var directory = new InMemoryNodeDirectory();
        await RegisterAsync(directory, "node-chat", "chat", NodeState.Ready, now);
        var discovery = new ClusterNodeDiscovery(directory);

        var node = await discovery.AnyAsync(new ClusterFeature("room"), TestContext.Current.CancellationToken);

        Assert.Null(node);
    }

    private static async ValueTask RegisterAsync(
        INodeDirectory directory,
        string node,
        string feature,
        NodeState state,
        DateTimeOffset now)
    {
        await directory.RegisterAsync(
            new NodeRegistration(
                "local",
                new NodeId(node),
                new Dictionary<string, NodeEndpoint>
                {
                    ["cluster"] = new NodeEndpoint($"tcp://{node}:21000")
                },
                [new NodeServiceDescriptor(feature)],
                now.AddMinutes(5),
                state),
            now);
    }
}
