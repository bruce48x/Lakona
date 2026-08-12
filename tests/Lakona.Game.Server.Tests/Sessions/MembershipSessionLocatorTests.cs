using Lakona.Game.Cluster;
using Lakona.Game.Server.Sessions;
using Xunit;

namespace Lakona.Game.Server.Tests.Sessions;

public sealed class MembershipSessionLocatorTests
{
    [Fact]
    public void Locator_resolves_exact_gateway_without_route_directory()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("31313131-3131-3131-3131-313131313131"));
        var gateway = new NodeReference(
            cluster,
            new NodeId("gateway-1"),
            new NodeIncarnationId(Guid.Parse("41414141-4141-4141-4141-414141414141")));
        var membership = new StubMembership(CreateSnapshot(gateway));
        var session = new GameSessionKey("player/110", MembershipSessionLocator.Encode(gateway));

        Assert.True(MembershipSessionLocator.TryResolve(session, membership, out var target));
        Assert.Equal(gateway, target!.NodeReference);
        Assert.Equal("tcp://gateway-1:21001", target.Endpoint.Address);

        membership.Current = CreateSnapshot(new NodeReference(
            cluster,
            gateway.Node,
            NodeIncarnationId.New()));
        Assert.False(MembershipSessionLocator.TryResolve(session, membership, out _));
    }

    [Fact]
    public void LocatorResolvesOnlyTheExactLiveGatewayIncarnation()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("31313131-3131-3131-3131-313131313131"));
        var gateway = new NodeReference(
            cluster,
            new NodeId("gateway-1"),
            new NodeIncarnationId(Guid.Parse("41414141-4141-4141-4141-414141414141")));
        var membership = new StubMembership(CreateSnapshot(gateway));
        var factory = new MembershipGameSessionIdFactory(membership, gateway.Node);
        var session = new GameSessionKey("player/110", factory.Create());
        Assert.True(MembershipSessionLocator.TryResolve(session, membership, out var resolved));
        Assert.Equal(gateway, resolved!.NodeReference);
        membership.Current = CreateSnapshot(new NodeReference(
            cluster,
            gateway.Node,
            NodeIncarnationId.New()));
        Assert.False(MembershipSessionLocator.TryResolve(session, membership, out _));
    }

    private static ClusterMembershipSnapshot CreateSnapshot(NodeReference gateway) => new(
        gateway.Cluster,
        new MembershipViewId(7),
        new[]
        {
            new ClusterMember(
                gateway,
                ClusterMemberState.Ready,
                new NodeEndpoint("tcp://gateway-1:21001"),
                isVoter: true)
        });

    private sealed class StubMembership : IClusterMembership
    {
        public StubMembership(ClusterMembershipSnapshot current) => Current = current;

        public ClusterMembershipSnapshot Current { get; set; }

        public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(
            MembershipViewId after,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
