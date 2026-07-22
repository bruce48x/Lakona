using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Cluster.Rpc.Serializer.Json;
using Lakona.Game.Cluster.Rpc.Transport.Tcp;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Shared.Interfaces;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class RemoteNotificationRelayExampleTests
{
    [Fact]
    public async Task MissingRouteIsReportedAsynchronouslyAfterAdmission()
    {
        await using var clientFactory = new ClusterClientFactory(CreateJsonClusterChannel());
        var session = new GameSessionKey("player-1", "session-a");
        await using var businessServices = CreateBusinessNotificationServices(
            new InMemoryRouteDirectory(),
            new ClusterClientNotificationDispatcher(clientFactory),
            "battle-1");
        var notifications = businessServices.GetRequiredService<IClientNotifications>();

        var status = notifications
            .ForSession<IPlayerCallback>(session)
            .OnMatchmakingStatus(new MatchmakingStatusUpdate());

        Assert.Equal(ClientNotificationStatus.Accepted, status);
    }

    [Fact]
    public async Task UnknownSessionRouteIsReportedAsynchronouslyAfterAdmission()
    {
        await using var clientFactory = new ClusterClientFactory(CreateJsonClusterChannel());
        var routes = new InMemoryRouteDirectory();
        var session = new GameSessionKey("player-1", "session-b");
        await routes.RegisterAsync(
            new RouteLocation(
                ClientNotificationRouteKey.FromSession(new GameSessionKey("player-1", "session-a")),
                new NodeId("gateway-1"),
                new NodeEndpoint("tcp://127.0.0.1:1"),
                DateTimeOffset.UtcNow.AddMinutes(1),
                generation: 1),
            TestContext.Current.CancellationToken);
        await using var businessServices = CreateBusinessNotificationServices(
            routes,
            new ClusterClientNotificationDispatcher(clientFactory),
            "battle-1");
        var notifications = businessServices.GetRequiredService<IClientNotifications>();

        var status = notifications
            .ForSession<IPlayerCallback>(session)
            .OnMatchmakingStatus(new MatchmakingStatusUpdate());

        Assert.Equal(ClientNotificationStatus.Accepted, status);
    }

    [Fact]
    public async Task RpcTransportFailureReturnsFailed()
    {
        var port = GetFreePort();
        await using var clientFactory = new ClusterClientFactory(CreateJsonClusterChannel());
        var dispatcher = new ClusterClientNotificationDispatcher(clientFactory);
        var session = new GameSessionKey("player-1", "session-a");
        var command = ClientNotificationCommandFactory.Create<IPlayerCallback>(
            session,
            callback => callback.OnMatchmakingStatus(new MatchmakingStatusUpdate()));

        var status = await dispatcher.DispatchAsync(
            new RouteLocation(
                ClientNotificationRouteKey.FromSession(session),
                new NodeId("gateway-1"),
                new NodeEndpoint($"tcp://127.0.0.1:{port}"),
                DateTimeOffset.UtcNow.AddMinutes(1),
                generation: 1),
            command!,
            TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Failed, status);
    }

    private static ServiceProvider CreateBusinessNotificationServices(
        IRouteDirectory routes,
        IClientNotificationRemoteDispatcher remoteDispatcher,
        string nodeId)
    {
        var services = new ServiceCollection();
        services.AddSingleton(routes);
        services.AddSingleton(remoteDispatcher);
        services.AddSingleton(new ClusterOptions
        {
            NodeId = nodeId
        });
        services.AddLakonaGameServerSessions();
        services.AddLakonaGameServerReliablePush();
        return services.BuildServiceProvider();
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

    private static ClusterRpcChannel CreateJsonClusterChannel() =>
        new(TcpClusterRpcTransport.Default, JsonClusterRpcSerializer.Default);
}
