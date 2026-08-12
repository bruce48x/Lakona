using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class ActorLocationLayoutTests
{
    [Fact]
    public void Layout_is_deterministic_and_uses_exact_ready_owner()
    {
        var cluster = new ClusterIncarnationId(Guid.Parse("10000000-0000-0000-0000-000000000000"));
        var nodeA = Reference(cluster, "node-a", 1);
        var nodeB = Reference(cluster, "node-b", 1);
        var snapshot = Snapshot(cluster, 4, nodeA, nodeB);

        var shard = ActorLocationLayout.GetShard(ActorId.From("room/42"));
        var owner = ActorLocationLayout.GetOwner(shard, snapshot);

        Assert.InRange(shard, 0, 1023);
        Assert.True(owner == nodeA || owner == nodeB);
        Assert.Equal(owner, ActorLocationLayout.GetOwner(shard, Snapshot(cluster, 5, nodeA, nodeB)));
    }

    [Fact]
    public void Descriptor_only_view_does_not_change_owner_but_incarnation_does()
    {
        var cluster = new ClusterIncarnationId(Guid.Parse("10000000-0000-0000-0000-000000000000"));
        var nodeA = Reference(cluster, "node-a", 1);
        var actor = ActorId.From("room/42");
        var shard = ActorLocationLayout.GetShard(actor);

        Assert.Equal(nodeA, ActorLocationLayout.GetOwner(shard, Snapshot(cluster, 1, nodeA)));
        Assert.Equal(nodeA, ActorLocationLayout.GetOwner(shard, Snapshot(cluster, 2, nodeA)));

        var restarted = Reference(cluster, "node-a", 2);
        Assert.Equal(restarted, ActorLocationLayout.GetOwner(shard, Snapshot(cluster, 3, restarted)));
        Assert.NotEqual(nodeA, restarted);
    }

    private static ClusterMembershipSnapshot Snapshot(
        ClusterIncarnationId cluster,
        long view,
        params NodeReference[] nodes) => new(
            cluster,
            new MembershipViewId(view),
            nodes.Select(node => new ClusterMember(
                node,
                ClusterMemberState.Ready,
                new NodeEndpoint($"tcp://{node.Node.Value}:21001"),
                isVoter: true)).ToArray());

    private static NodeReference Reference(ClusterIncarnationId cluster, string node, int incarnation) => new(
        cluster,
        new NodeId(node),
        new NodeIncarnationId(Guid.Parse($"{incarnation:D8}-0000-0000-0000-000000000000")));
}
