using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Actors;
using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests.Cluster.Actors;

public sealed class ActorDirectoryRingTests
{
    [Fact]
    public void One_node_owns_the_full_ring_across_its_virtual_partitions()
    {
        var node = Reference("node-a", 1);
        var ring = new ActorDirectoryRing(Snapshot(1, node));

        for (var index = 0; index < 10_000; index++)
            Assert.Equal(node, ring.GetOwner(ActorId.From($"room/{index}")).Owner);

        var ranges = Enumerable.Range(0, ActorDirectoryRing.DefaultPartitionsPerNode)
            .Select(index => ring.GetRange(new ActorDirectoryPartitionId(node, index)))
            .Where(range => !range.IsEmpty)
            .ToArray();
        Assert.Equal(ActorDirectoryRing.DefaultPartitionsPerNode, ranges.Length);
    }

    [Fact]
    public void Every_actor_has_the_same_deterministic_owner()
    {
        var nodes = new[] { Reference("node-c", 3), Reference("node-a", 1), Reference("node-b", 2) };
        var first = new ActorDirectoryRing(Snapshot(7, nodes));
        var second = new ActorDirectoryRing(Snapshot(7, nodes.Reverse().ToArray()));

        for (var index = 0; index < 10_000; index++)
        {
            var actor = ActorId.From($"player/{index}");
            Assert.Equal(first.GetOwner(actor), second.GetOwner(actor));
        }
    }

    [Fact]
    public void Adding_a_node_moves_only_actors_to_the_added_node()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var before = new ActorDirectoryRing(Snapshot(4, nodeA));
        var after = new ActorDirectoryRing(Snapshot(5, nodeA, nodeB));
        var moved = 0;

        for (var index = 0; index < 20_000; index++)
        {
            var actor = ActorId.From($"room/{index}");
            var oldOwner = before.GetOwner(actor).Owner;
            var newOwner = after.GetOwner(actor).Owner;
            if (oldOwner == newOwner) continue;
            moved++;
            Assert.Equal(nodeB, newOwner);
        }

        Assert.InRange(moved, 7_000, 13_000);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(500)]
    public void Large_cluster_virtual_partitions_form_one_deterministic_complete_ring(int nodeCount)
    {
        var nodes = Enumerable.Range(1, nodeCount)
            .Select(index => Reference($"node-{index:000}", index))
            .ToArray();
        var first = new ActorDirectoryRing(Snapshot(7, nodes));
        var reversed = new ActorDirectoryRing(Snapshot(7, nodes.Reverse().ToArray()));
        var ranges = nodes
            .SelectMany(node => Enumerable.Range(0, ActorDirectoryRing.DefaultPartitionsPerNode)
                .Select(index => first.GetRange(new ActorDirectoryPartitionId(node, index))))
            .Where(static range => !range.IsEmpty)
            .ToArray();

        Assert.Equal(nodeCount * ActorDirectoryRing.DefaultPartitionsPerNode, ranges.Length);
        var byStart = ranges.ToDictionary(static range => range.Start);
        var firstStart = ranges[0].Start;
        var current = firstStart;
        var visited = 0;
        do
        {
            current = byStart[current].End;
            visited++;
        } while (current != firstStart && visited <= ranges.Length);

        Assert.Equal(ranges.Length, visited);
        for (var index = 0; index < 20_000; index++)
        {
            var actor = ActorId.From($"large-cluster/{index}");
            Assert.Equal(first.GetOwner(actor), reversed.GetOwner(actor));
        }
    }

    private static readonly ClusterIncarnationId Cluster = new(
        Guid.Parse("10000000-0000-0000-0000-000000000000"));

    private static ClusterMembershipSnapshot Snapshot(long view, params NodeReference[] nodes) => new(
        Cluster,
        new MembershipViewId(view),
        nodes.Select(node => new ClusterMember(
            node,
            ClusterMemberState.Active,
            new NodeEndpoint($"tcp://{node.Node.Value}:21001"))).ToArray());

    private static NodeReference Reference(string node, int incarnation) => new(
        Cluster,
        new NodeId(node),
        new NodeIncarnationId(Guid.Parse($"{incarnation:D8}-0000-0000-0000-000000000000")));
}
