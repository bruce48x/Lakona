using Lakona.Game.Cluster;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class MembershipClusterNodeDiscoveryTests
{
    [Fact]
    public async Task DiscoveryReadsOnlyReadyMatchingMembersFromTheCurrentSnapshot()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("12341234-1111-2222-3333-123412341234"));
        var room = CreateMember(cluster, "battle-1", ClusterMemberState.Ready, "battle");
        var recovering = CreateMember(
            cluster,
            "battle-2",
            ClusterMemberState.Recovering,
            "battle");
        var gateway = CreateMember(cluster, "gateway-1", ClusterMemberState.Ready, "gateway");
        var membership = new FixedMembership(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(4),
            new[] { recovering, gateway, room }));
        IClusterNodeDiscovery discovery = new MembershipClusterNodeDiscovery(membership);

        var nodes = await discovery.QueryAsync(
            new ClusterNodeDiscoveryQuery(
                actorHostName: "RoomActor",
                actorHostPolicyHash: "policy",
                labels: new Dictionary<string, string> { ["role"] = "battle" }),
            TestContext.Current.CancellationToken);

        var descriptor = Assert.Single(nodes);
        Assert.Equal(room.Reference, descriptor.Reference);
        Assert.Equal(new NodeId("battle-1"), descriptor.Node);
        Assert.Equal("tcp://battle-1:21001", descriptor.Endpoints["cluster"].Address);
        Assert.Equal("RoomActor", Assert.Single(descriptor.ActorHosts).Actor);
    }

    private static ClusterMember CreateMember(
        ClusterIncarnationId cluster,
        string node,
        ClusterMemberState state,
        string role)
    {
        return new ClusterMember(
            new NodeReference(cluster, new NodeId(node), NodeIncarnationId.New()),
            state,
            new NodeEndpoint($"tcp://{node}:21001"),
            isVoter: true,
            new Dictionary<string, string> { ["role"] = role },
            new[] { new NodeActorHostDescriptor("RoomActor", "policy", "build") },
            startupActors: null);
    }

    private sealed class FixedMembership : IClusterMembership
    {
        public FixedMembership(ClusterMembershipSnapshot current)
        {
            Current = current;
        }

        public ClusterMembershipSnapshot Current { get; }

        public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(
            MembershipViewId after,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
