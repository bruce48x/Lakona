using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Actors;
using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests.Cluster.Actors;

public sealed class ActorLocationLayoutTests
{
    [Theory]
    [InlineData("room/42", "6BD29C2D208AECD0536E107636EF5E24DD8FE3C1B46B68E92727E5D0445BB46B", 208)]
    [InlineData("user/player-123", "5C28F81FC03DC4F222EACF890F111633132B9DDA6863F7BBECBEBEF28D2432D3", 242)]
    [InlineData("@startup-affinity/test/key", "F106A2A71B11B5999CAE78A8F117A098D890B14367DF4911474984BE56FEC022", 409)]
    public void Shard_layout_matches_frozen_sha256_vectors(string actorId, string digest, int shard)
    {
        var id = ActorId.From(actorId);

        Assert.Equal(digest, Convert.ToHexString(ActorLocationLayout.GetShardDigest(id)));
        Assert.Equal(shard, ActorLocationLayout.GetShard(id));
    }

    [Fact]
    public void Owner_layout_matches_frozen_score_and_tie_break_vectors()
    {
        const int shard = 208;
        var cluster = new ClusterIncarnationId(Guid.Parse("10000000-0000-0000-0000-000000000000"));
        var nodeA = Reference(cluster, "node-a", 1);
        var nodeB = Reference(cluster, "node-b", 1);
        var nodeZ = Reference(cluster, "node-z", 1);

        Assert.Equal(3_789_677_293_983_529_320UL, ActorLocationLayout.GetOwnerScore(shard, nodeA.Node));
        Assert.Equal(14_196_333_126_012_546_876UL, ActorLocationLayout.GetOwnerScore(shard, nodeB.Node));
        Assert.Equal(8_869_890_782_629_806_550UL, ActorLocationLayout.GetOwnerScore(shard, nodeZ.Node));
        Assert.Equal(nodeB, ActorLocationLayout.GetOwner(shard, Snapshot(cluster, 1, nodeA, nodeB, nodeZ)));
    }

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
