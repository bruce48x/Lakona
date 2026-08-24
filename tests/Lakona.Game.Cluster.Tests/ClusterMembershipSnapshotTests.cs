using Lakona.Game.Cluster;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class ClusterMembershipSnapshotTests
{
    [Fact]
    public async Task UninitializedStateRejectsReadsAndWaiters()
    {
        IClusterMembership membership = new ClusterMembershipState();

        Assert.Throws<InvalidOperationException>(() => membership.Current);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await membership.WaitForChangeAsync(
                new MembershipViewId(0),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public void SnapshotCanonicalizesAndResolvesExactNodeReferences()
    {
        var cluster = new ClusterIncarnationId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var nodeA = new NodeReference(
            cluster,
            new NodeId("data-a"),
            new NodeIncarnationId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")));
        var nodeB = new NodeReference(
            cluster,
            new NodeId("data-b"),
            new NodeIncarnationId(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")));
        var members = new List<ClusterMember>
        {
            CreateMember(nodeB, "tcp://127.0.0.1:21002"),
            CreateMember(nodeA, "tcp://127.0.0.1:21001")
        };

        var snapshot = new ClusterMembershipSnapshot(cluster, new MembershipViewId(7), members);
        members.Clear();

        Assert.Equal([nodeA, nodeB], snapshot.Members.Select(static member => member.Reference));
        Assert.True(snapshot.TryGetMember(nodeA, out var resolved));
        Assert.Equal("tcp://127.0.0.1:21001", resolved!.ClusterEndpoint.Address);

        var restartedNodeA = new NodeReference(
            cluster,
            new NodeId("data-a"),
            new NodeIncarnationId(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")));
        Assert.False(snapshot.TryGetMember(restartedNodeA, out _));
    }

    private static ClusterMember CreateMember(NodeReference reference, string endpoint) =>
        new(reference, ClusterMemberState.Active, new NodeEndpoint(endpoint));
}
