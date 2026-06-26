using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Server;
using Lakona.Rpc.Transport.Tcp;
using Shared.Interfaces;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class RemoteNotificationRelayExampleTests
{
    [Fact]
    public async Task RemoteMatchmakingNotificationCanRelayToGatewayCallback()
    {
        var gatewayPort = GetFreePort();
        var gatewaySessions = new InMemoryGameSessionRegistry();
        var session = await gatewaySessions.StartNewSessionAsync("player-1", TestContext.Current.CancellationToken);
        var callback = new CapturingControlCallback();
        await gatewaySessions.BindSessionAsync(session, "control-1", callback, TestContext.Current.CancellationToken);
        using var stopGateway = new CancellationTokenSource();
        var builder = RpcServerHostBuilder.Create()
            .UseSerializer(new JsonRpcSerializer())
            .UseAcceptor(new TcpConnectionAcceptor(gatewayPort, "127.0.0.1"));
        ClientNotificationCommandBinder.Bind(
            builder.ServiceRegistry,
            new LocalClientNotificationCommandDispatcher(gatewaySessions));
        var gatewayTask = builder.RunAsync(stopGateway.Token).AsTask();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        var routes = new InMemoryRouteDirectory();
        var registrar = new ClientSessionRouteRegistrar(
            routes,
            new NodeId("gateway-1"),
            new NodeEndpoint($"tcp://127.0.0.1:{gatewayPort}"));
        await registrar.RegisterAsync(session, TestContext.Current.CancellationToken);
        await using var clientFactory = new ClusterClientFactory(
            new TcpClusterTransportFactory(),
            new JsonRpcSerializer());
        var remoteRelay = new ClientNotificationRelay(
            new InMemoryGameSessionRegistry(),
            routes,
            new ClusterClientNotificationDispatcher(clientFactory),
            new NodeId("battle-1"));

        var update = new MatchmakingStatusUpdate
        {
            State = MatchmakingState.Matched,
            RoomId = "room-1",
            MatchedPlayerCount = 2,
            Message = "Matched into room room-1"
        };

        var status = await remoteRelay.NotifyAsync<IControlCallback>(
            session,
            target => target.OnMatchmakingStatus(update),
            TestContext.Current.CancellationToken);

        stopGateway.Cancel();
        await Task.WhenAny(gatewayTask, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        Assert.Equal(ClientNotificationStatus.Delivered, status);
        Assert.Equal(MatchmakingState.Matched, callback.LastMatchmakingStatus?.State);
        Assert.Equal("room-1", callback.LastMatchmakingStatus?.RoomId);
        Assert.Equal(2, callback.LastMatchmakingStatus?.MatchedPlayerCount);
        Assert.Equal("Matched into room room-1", callback.LastMatchmakingStatus?.Message);
    }

    [Fact]
    public async Task RemoteRealtimeNotificationCanRelayToGatewayCallback()
    {
        var gatewayPort = GetFreePort();
        var gatewaySessions = new InMemoryGameSessionRegistry();
        var session = await gatewaySessions.StartNewSessionAsync("player-1", TestContext.Current.CancellationToken);
        var callback = new CapturingBattleCallback();
        await gatewaySessions.BindSessionAsync(session, "realtime-1", callback, TestContext.Current.CancellationToken);
        using var stopGateway = new CancellationTokenSource();
        var builder = RpcServerHostBuilder.Create()
            .UseSerializer(new JsonRpcSerializer())
            .UseAcceptor(new TcpConnectionAcceptor(gatewayPort, "127.0.0.1"));
        ClientNotificationCommandBinder.Bind(
            builder.ServiceRegistry,
            new LocalClientNotificationCommandDispatcher(gatewaySessions));
        var gatewayTask = builder.RunAsync(stopGateway.Token).AsTask();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        var routes = new InMemoryRouteDirectory();
        var registrar = new ClientSessionRouteRegistrar(
            routes,
            new NodeId("gateway-1"),
            new NodeEndpoint($"tcp://127.0.0.1:{gatewayPort}"));
        await registrar.RegisterAsync(session, TestContext.Current.CancellationToken);
        await using var clientFactory = new ClusterClientFactory(
            new TcpClusterTransportFactory(),
            new JsonRpcSerializer());
        var remoteRelay = new ClientNotificationRelay(
            new InMemoryGameSessionRegistry(),
            routes,
            new ClusterClientNotificationDispatcher(clientFactory),
            new NodeId("battle-1"));

        var worldState = new WorldState
        {
            Tick = 42,
            RoundRemainingSeconds = 15
        };

        var status = await remoteRelay.NotifyAsync<IBattleCallback>(
            session,
            target => target.OnWorldState(worldState),
            TestContext.Current.CancellationToken);

        stopGateway.Cancel();
        await Task.WhenAny(gatewayTask, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        Assert.Equal(ClientNotificationStatus.Delivered, status);
        Assert.Equal(42, callback.LastWorldState?.Tick);
        Assert.Equal(15, callback.LastWorldState?.RoundRemainingSeconds);
    }

    [Fact]
    public async Task MissingRouteReturnsRouteNotFound()
    {
        await using var clientFactory = new ClusterClientFactory(
            new TcpClusterTransportFactory(),
            new JsonRpcSerializer());
        var session = new GameSessionKey("player-1", "session-a", 1);
        var relay = new ClientNotificationRelay(
            new InMemoryGameSessionRegistry(),
            new InMemoryRouteDirectory(),
            new ClusterClientNotificationDispatcher(clientFactory),
            new NodeId("battle-1"));

        var status = await relay.NotifyAsync<IControlCallback>(
            session,
            callback => callback.OnMatchmakingStatus(new MatchmakingStatusUpdate()),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.RouteNotFound, status);
    }

    [Fact]
    public async Task StaleRouteGenerationReturnsRouteNotFound()
    {
        await using var clientFactory = new ClusterClientFactory(
            new TcpClusterTransportFactory(),
            new JsonRpcSerializer());
        var routes = new InMemoryRouteDirectory();
        var session = new GameSessionKey("player-1", "session-a", 2);
        await routes.RegisterAsync(
            new RouteLocation(
                ClientNotificationRouteKey.FromSession(new GameSessionKey("player-1", "session-a", 1)),
                new NodeId("gateway-1"),
                new NodeEndpoint("tcp://127.0.0.1:1"),
                DateTimeOffset.UtcNow.AddMinutes(1),
                generation: 1),
            TestContext.Current.CancellationToken);
        var relay = new ClientNotificationRelay(
            new InMemoryGameSessionRegistry(),
            routes,
            new ClusterClientNotificationDispatcher(clientFactory),
            new NodeId("battle-1"));

        var status = await relay.NotifyAsync<IControlCallback>(
            session,
            callback => callback.OnMatchmakingStatus(new MatchmakingStatusUpdate()),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.RouteNotFound, status);
    }

    [Fact]
    public async Task MissingGatewayCallbackReturnsCallbackUnavailable()
    {
        var gatewayPort = GetFreePort();
        using var stopGateway = new CancellationTokenSource();
        var builder = RpcServerHostBuilder.Create()
            .UseSerializer(new JsonRpcSerializer())
            .UseAcceptor(new TcpConnectionAcceptor(gatewayPort, "127.0.0.1"));
        ClientNotificationCommandBinder.Bind(
            builder.ServiceRegistry,
            new LocalClientNotificationCommandDispatcher(new InMemoryGameSessionRegistry()));
        var gatewayTask = builder.RunAsync(stopGateway.Token).AsTask();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        await using var clientFactory = new ClusterClientFactory(
            new TcpClusterTransportFactory(),
            new JsonRpcSerializer());
        var routes = new InMemoryRouteDirectory();
        var session = new GameSessionKey("player-1", "session-a", 1);
        await routes.RegisterAsync(
            new RouteLocation(
                ClientNotificationRouteKey.FromSession(session),
                new NodeId("gateway-1"),
                new NodeEndpoint($"tcp://127.0.0.1:{gatewayPort}"),
                DateTimeOffset.UtcNow.AddMinutes(1),
                generation: session.Generation),
            TestContext.Current.CancellationToken);
        var relay = new ClientNotificationRelay(
            new InMemoryGameSessionRegistry(),
            routes,
            new ClusterClientNotificationDispatcher(clientFactory),
            new NodeId("battle-1"));

        var status = await relay.NotifyAsync<IControlCallback>(
            session,
            callback => callback.OnMatchmakingStatus(new MatchmakingStatusUpdate()),
            TestContext.Current.CancellationToken);

        stopGateway.Cancel();
        await Task.WhenAny(gatewayTask, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        Assert.Equal(ClientNotificationStatus.CallbackUnavailable, status);
    }

    [Fact]
    public async Task RpcTransportFailureReturnsFailed()
    {
        var port = GetFreePort();
        await using var clientFactory = new ClusterClientFactory(
            new TcpClusterTransportFactory(),
            new JsonRpcSerializer());
        var dispatcher = new ClusterClientNotificationDispatcher(clientFactory);
        var session = new GameSessionKey("player-1", "session-a", 1);
        var command = ClientNotificationCommandFactory.Create<IControlCallback>(
            session,
            callback => callback.OnMatchmakingStatus(new MatchmakingStatusUpdate()));

        var status = await dispatcher.DispatchAsync(
            new RouteLocation(
                ClientNotificationRouteKey.FromSession(session),
                new NodeId("gateway-1"),
                new NodeEndpoint($"tcp://127.0.0.1:{port}"),
                DateTimeOffset.UtcNow.AddMinutes(1),
                generation: session.Generation),
            command!,
            TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Failed, status);
    }

    private sealed class CapturingControlCallback : IControlCallback
    {
        public MatchmakingStatusUpdate? LastMatchmakingStatus { get; private set; }

        public void OnMatchmakingStatus(MatchmakingStatusUpdate matchmakingStatus)
        {
            LastMatchmakingStatus = matchmakingStatus;
        }
    }

    private sealed class CapturingBattleCallback : IBattleCallback
    {
        public WorldState? LastWorldState { get; private set; }

        public void OnWorldState(WorldState worldState)
        {
            LastWorldState = worldState;
        }

        public void OnPlayerDead(PlayerDead playerDead)
        {
        }

        public void OnMatchEnd(MatchEnd matchEnd)
        {
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
