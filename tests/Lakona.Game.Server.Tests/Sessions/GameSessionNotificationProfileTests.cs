using Lakona.Game.Abstractions;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Sessions;
using Lakona.Game.Server.Tests.Testing;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Server;
using Lakona.Rpc.Transport.Loopback;
using Xunit;

namespace Lakona.Game.Server.Tests.Sessions;

public sealed class GameSessionNotificationProfileTests
{
    [Fact]
    public async Task Only_a_committed_profile_can_be_retained_across_reconnect()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new Clock();
        var sessions = new InMemoryGameSessionRegistry(new LakonaGameHostingOptions(), clock);
        var session = await sessions.StartNewSessionAsync("player", ct);

        await sessions.PrepareProfiledSessionBindingAsync(session, "first", "json", ct);
        Assert.Null(await sessions.GetNotificationSerializerProfileAsync(session, ct));
        await sessions.RollbackSessionBindingAsync(session, "first", ct);
        await sessions.PrepareProfiledSessionBindingAsync(session, "second", "binary-v2", ct);
        await sessions.CommitSessionBindingAsync(session, "second", ct);
        await sessions.MarkConnectionDisconnectedAsync("second", ct);
        Assert.Equal("binary-v2", await sessions.GetNotificationSerializerProfileAsync(session, ct));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sessions.PrepareProfiledSessionBindingAsync(session, "wrong", "json", ct).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sessions.BindSessionAsync(session, "unprofiled", ct).AsTask());
        await sessions.PrepareProfiledSessionBindingAsync(session, "third", "binary-v2", ct);
        await sessions.RollbackSessionBindingAsync(session, "third", ct);
        Assert.Equal("binary-v2", await sessions.GetNotificationSerializerProfileAsync(session, ct));
        clock.UtcNow = clock.UtcNow.AddDays(1);
        Assert.Null(await sessions.GetNotificationSerializerProfileAsync(session, ct));
    }

    [Fact]
    public async Task Remote_query_uses_exact_gateway_and_caches_only_committed_profiles()
    {
        var ct = TestContext.Current.CancellationToken;
        var gateway = new NodeReference(ClusterIncarnationId.New(), new NodeId("edge"), NodeIncarnationId.New());
        var membership = new TestClusterMembership(new ClusterMembershipSnapshot(gateway.Cluster,
            new MembershipViewId(1), [new ClusterMember(gateway, ClusterMemberState.Active, new NodeEndpoint("tcp://edge:21001"))]));
        var sessions = new InMemoryGameSessionRegistry(new LakonaGameHostingOptions(), TimeProvider.System,
            new MembershipGameSessionIdFactory(membership, gateway.Node));
        var session = await sessions.StartNewSessionAsync("player", ct);
        Assert.True(MembershipSessionLocator.TryResolve(session, membership, out var route));
        using var owner = new GameSessionNotificationProfileLookup(sessions);
        var registry = new RpcServiceRegistry();
        SessionNotificationProfileProtocol.Bind(registry, owner, membership, gateway.Node);
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        await using var server = TestRpcSession.Create(serverTransport, new MemoryPackRpcSerializer(), true, registry);
        await using var client = new RpcClientRuntime(clientTransport, new MemoryPackRpcSerializer());
        await server.StartAsync(ct);
        await client.StartAsync(ct);
        var factory = new CountingClientFactory(client);
        using var publisher = new GameSessionNotificationProfileLookup(new InMemoryGameSessionRegistry(), factory);

        Assert.Null(await publisher.GetRemoteAsync(route!, session, ct));
        await sessions.PrepareProfiledSessionBindingAsync(session, "active", "binary-v2", ct);
        await sessions.CommitSessionBindingAsync(session, "active", ct);
        await sessions.MarkConnectionDisconnectedAsync("active", ct);
        Assert.Equal("binary-v2", await publisher.GetRemoteAsync(route!, session, ct));
        Assert.Equal("binary-v2", await publisher.GetRemoteAsync(route!, session, ct));
        Assert.Equal(2, factory.Calls);
        var wrongOwner = new GameSessionKey("other", session.SessionId);
        Assert.Null(await publisher.GetRemoteAsync(route!, wrongOwner, ct));

        var replacement = new NodeReference(gateway.Cluster, gateway.Node, NodeIncarnationId.New());
        var wrongRoute = new RouteLocation(route!.Route, replacement, route.MembershipView, route.Endpoint);
        Assert.Null(await publisher.GetRemoteAsync(wrongRoute, session, ct));
        await sessions.MarkSessionTerminatedAsync(session, new SessionTerminationNotice(SessionTerminationReason.Policy), true, ct);
        Assert.Null(await owner.GetLocalAsync(session, ct));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class CountingClientFactory(IRpcClient client) : IClusterClientFactory
    {
        public int Calls { get; private set; }
        public ValueTask<IRpcClient> GetClientAsync(RouteLocation route, CancellationToken cancellationToken = default)
        {
            Calls++;
            return new ValueTask<IRpcClient>(client);
        }
    }
}
