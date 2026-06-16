using Lakona.Game.Cluster;
using Lakona.Game.Server.Sessions;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class ClientNotificationRelayTests
{
    [Fact]
    public void RouteKeyIncludesOwnerSessionAndGeneration()
    {
        var session = new GameSessionKey("player-1", "session-a", 7);

        var route = ClientNotificationRouteKey.FromSession(session);

        Assert.Equal("client-session:player-1/session-a/7", route.Value);
    }

    [Fact]
    public async Task RelayInvokesLocalCallbackOnGateway()
    {
        var directory = new InMemoryGameSessionDirectory();
        var session = await directory.StartNewSessionAsync("player-1", TestContext.Current.CancellationToken);
        var callback = new TestPlayerCallback();
        await directory.BindSessionAsync(session, "conn-1", callback, TestContext.Current.CancellationToken);
        var relay = new ClientNotificationRelay(directory);

        var status = await relay.NotifyAsync<TestPlayerCallback>(
            session,
            cb => cb.Notify("hello"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Delivered, status);
        Assert.Equal("hello", callback.LastMessage);
    }

    [Fact]
    public async Task RelayReturnsRouteNotFoundForStaleGeneration()
    {
        var directory = new InMemoryGameSessionDirectory();
        var current = await directory.StartNewSessionAsync("player-1", TestContext.Current.CancellationToken);
        var stale = new GameSessionKey(current.OwnerKey, current.SessionId, current.Generation + 1);
        var relay = new ClientNotificationRelay(directory);

        var status = await relay.NotifyAsync<TestPlayerCallback>(
            stale,
            cb => cb.Notify("stale"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.RouteNotFound, status);
    }

    [Fact]
    public async Task RegistrarRegistersGatewayOwnedClientSessionRoute()
    {
        var routes = new CapturingRouteDirectory();
        var registrar = new ClientSessionRouteRegistrar(
            routes,
            new NodeId("gateway-1"),
            new NodeEndpoint("tcp://10.0.0.2:21002"));
        var session = new GameSessionKey("player-1", "session-a", 7);

        await registrar.RegisterAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal("client-session:player-1/session-a/7", routes.LastRoute);
        Assert.Equal(new NodeId("gateway-1"), routes.LastNode);
        Assert.Equal("tcp://10.0.0.2:21002", routes.LastEndpoint);
    }

    private sealed class TestPlayerCallback
    {
        public string LastMessage { get; private set; } = "";

        public void Notify(string message)
        {
            LastMessage = message;
        }
    }

    private sealed class CapturingRouteDirectory : IRouteDirectory
    {
        public string LastRoute { get; private set; } = "";

        public NodeId LastNode { get; private set; }

        public string LastEndpoint { get; private set; } = "";

        public ValueTask<RouteRegistrationStatus> RegisterAsync(
            RouteLocation location,
            CancellationToken cancellationToken = default)
        {
            LastRoute = location.Route.Value;
            LastNode = location.Node;
            LastEndpoint = location.Endpoint.Address;
            return new ValueTask<RouteRegistrationStatus>(RouteRegistrationStatus.Registered);
        }

        public ValueTask<RouteUnregisterStatus> UnregisterAsync(
            RouteKey route,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<RouteUnregisterStatus>(RouteUnregisterStatus.Removed);
        }

        public ValueTask<RouteLocation?> ResolveAsync(
            RouteKey route,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<RouteLeaseRefreshStatus> RefreshLeaseAsync(
            RouteLocation expectedLocation,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<int> ExpireAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<int> ClearByNodeAsync(
            NodeId node,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<int> ClearByNodeEpochAsync(
            NodeId node,
            long nodeEpoch,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
