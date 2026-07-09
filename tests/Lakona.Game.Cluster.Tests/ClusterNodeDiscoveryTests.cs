using Lakona.Game.Cluster;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class ClusterNodeDiscoveryTests
{
    [Fact]
    public async Task ListAsync_returns_ready_nodes_that_match_labels()
    {
        var now = DateTimeOffset.UtcNow;
        var directory = new InMemoryNodeDirectory();
        await RegisterAsync(directory, "node-room-a", "room", NodeState.Ready, now);
        await RegisterAsync(directory, "node-room-b", "room", NodeState.Ready, now);
        await RegisterAsync(directory, "node-chat", "chat", NodeState.Ready, now);
        await RegisterAsync(directory, "node-starting", "room", NodeState.Starting, now);
        var discovery = new ClusterNodeDiscovery(directory);

        var nodes = await discovery.ListAsync(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["role"] = "room" },
            TestContext.Current.CancellationToken);

        Assert.Collection(
            nodes,
            node =>
            {
                Assert.Equal(new NodeId("node-room-a"), node.Node);
                Assert.Equal(NodeState.Ready, node.State);
                Assert.Equal("room", node.Labels["role"]);
                Assert.Equal("tcp://node-room-a:21000", node.Endpoints["cluster"].Address);
            },
            node =>
            {
                Assert.Equal(new NodeId("node-room-b"), node.Node);
                Assert.Equal(NodeState.Ready, node.State);
                Assert.Equal("room", node.Labels["role"]);
                Assert.Equal("tcp://node-room-b:21000", node.Endpoints["cluster"].Address);
            });
    }

    [Fact]
    public async Task AnyAsync_returns_first_ready_node_for_labels()
    {
        var now = DateTimeOffset.UtcNow;
        var directory = new InMemoryNodeDirectory();
        await RegisterAsync(directory, "node-b", "room", NodeState.Ready, now);
        await RegisterAsync(directory, "node-a", "room", NodeState.Ready, now);
        var discovery = new ClusterNodeDiscovery(directory);

        var node = await discovery.AnyAsync(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["role"] = "room" },
            TestContext.Current.CancellationToken);

        Assert.NotNull(node);
        Assert.Equal(new NodeId("node-a"), node!.Node);
    }

    [Fact]
    public async Task AnyAsync_returns_null_when_label_is_missing()
    {
        var now = DateTimeOffset.UtcNow;
        var directory = new InMemoryNodeDirectory();
        await RegisterAsync(directory, "node-chat", "chat", NodeState.Ready, now);
        var discovery = new ClusterNodeDiscovery(directory);

        var node = await discovery.AnyAsync(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["role"] = "room" },
            TestContext.Current.CancellationToken);

        Assert.Null(node);
    }

    private static async ValueTask RegisterAsync(
        INodeDirectory directory,
        string node,
        string role,
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
                now.AddMinutes(5),
                state,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["role"] = role
                }),
            now);
    }
}
