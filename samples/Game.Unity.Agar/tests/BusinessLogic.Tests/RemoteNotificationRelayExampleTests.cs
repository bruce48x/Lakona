using Lakona.Game.Cluster;
using Lakona.Game.Server.Sessions;
using Shared.Interfaces;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class RemoteNotificationRelayExampleTests
{
    [Fact]
    public async Task RemoteMatchmakingNotificationCanRelayToGatewayCallback()
    {
        var gatewaySessions = new InMemoryGameSessionDirectory();
        var session = await gatewaySessions.StartNewSessionAsync("player-1", TestContext.Current.CancellationToken);
        var callback = new CapturingPlayerCallback();
        await gatewaySessions.BindSessionAsync(session, "control-1", callback, TestContext.Current.CancellationToken);
        var routes = new InMemoryRouteDirectory();
        var gatewayDispatcher = new LocalClientNotificationCommandDispatcher(gatewaySessions);
        var registrar = new ClientSessionRouteRegistrar(
            routes,
            new NodeId("gateway-1"),
            new NodeEndpoint("tcp://gateway-1:21002"));
        await registrar.RegisterAsync(session, TestContext.Current.CancellationToken);
        var remoteRelay = new ClientNotificationRelay(
            new InMemoryGameSessionDirectory(),
            routes,
            new GatewayProcessNotificationDispatcher(gatewayDispatcher),
            new NodeId("battle-1"));

        var update = new MatchmakingStatusUpdate
        {
            State = MatchmakingState.Matched,
            RoomId = "room-1",
            MatchedPlayerCount = 2,
            Message = "Matched into room room-1"
        };

        var status = await remoteRelay.NotifyAsync<IPlayerCallback>(
            session,
            target => target.OnMatchmakingStatus(update),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Delivered, status);
        Assert.Equal(MatchmakingState.Matched, callback.LastMatchmakingStatus?.State);
        Assert.Equal("room-1", callback.LastMatchmakingStatus?.RoomId);
        Assert.Equal(2, callback.LastMatchmakingStatus?.MatchedPlayerCount);
        Assert.Equal("Matched into room room-1", callback.LastMatchmakingStatus?.Message);
    }

    private sealed class GatewayProcessNotificationDispatcher : IClientNotificationRemoteDispatcher
    {
        private readonly LocalClientNotificationCommandDispatcher _gatewayDispatcher;

        public GatewayProcessNotificationDispatcher(LocalClientNotificationCommandDispatcher gatewayDispatcher)
        {
            _gatewayDispatcher = gatewayDispatcher;
        }

        public ValueTask<ClientNotificationStatus> DispatchAsync(
            RouteLocation target,
            ClientNotificationCommand command,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(new NodeId("gateway-1"), target.Node);
            Assert.Equal("tcp://gateway-1:21002", target.Endpoint.Address);
            Assert.Empty(target.Metadata);
            return _gatewayDispatcher.DispatchAsync(command, cancellationToken);
        }
    }

    private sealed class CapturingPlayerCallback : IPlayerCallback
    {
        public MatchmakingStatusUpdate? LastMatchmakingStatus { get; private set; }

        public void OnWorldState(WorldState worldState)
        {
        }

        public void OnPlayerDead(PlayerDead deadEvent)
        {
        }

        public void OnMatchEnd(MatchEnd matchEnd)
        {
        }

        public void OnMatchmakingStatus(MatchmakingStatusUpdate matchmakingStatus)
        {
            LastMatchmakingStatus = matchmakingStatus;
        }
    }
}
