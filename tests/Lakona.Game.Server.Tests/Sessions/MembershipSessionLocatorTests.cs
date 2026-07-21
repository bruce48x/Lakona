using Lakona.Game.Cluster;
using Lakona.Game.Server.Sessions;
using Xunit;

namespace Lakona.Game.Server.Tests.Sessions;

public sealed class MembershipSessionLocatorTests
{
    [Fact]
    public async Task LocatorResolvesOnlyTheExactLiveGatewayIncarnation()
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
        var routes = new MembershipSessionRouteDirectory(
            new InMemoryRouteDirectory(),
            membership);

        var resolved = await routes.ResolveAsync(
            ClientNotificationRouteKey.FromSession(session),
            DateTimeOffset.UtcNow,
            TestContext.Current.CancellationToken);

        Assert.NotNull(resolved);
        Assert.Equal(gateway, resolved.NodeReference);
        membership.Current = CreateSnapshot(new NodeReference(
            cluster,
            gateway.Node,
            NodeIncarnationId.New()));
        Assert.Null(await routes.ResolveAsync(
            ClientNotificationRouteKey.FromSession(session),
            DateTimeOffset.UtcNow,
            TestContext.Current.CancellationToken));
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
