using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Server;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class GameSessionLifecycleBridgeTests
{
    [Fact]
    public async Task StartSessionPublishesEndpointBoundOnceForActiveEndpointAggregate()
    {
        var handler = new RecordingLifecycleHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IGameSessionLifecycleHandler>(handler);
        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();

        var session = await server.StartSessionAsync(
            "player-a",
            GameEndpointName.Control,
            "connection-a",
            new LoginCallback(),
            TestContext.Current.CancellationToken);
        await server.BindEndpointAsync(
            session,
            GameEndpointName.Control,
            "connection-a",
            new ChatCallback(),
            TestContext.Current.CancellationToken);
        await server.BindEndpointAsync(
            session,
            GameEndpointName.Control,
            "connection-b",
            new LoginCallback(),
            TestContext.Current.CancellationToken);

        var bound = Assert.Single(handler.EndpointBound);
        Assert.Equal(new SessionEndpointKey(session, GameEndpointName.Control), bound.Endpoint);
        Assert.Equal("connection-a", bound.ConnectionId);
    }

    [Fact]
    public async Task ResumeSessionPublishesEndpointBoundWhenDisconnectedAggregateBecomesActive()
    {
        var handler = new RecordingLifecycleHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IGameSessionLifecycleHandler>(handler);
        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();

        var session = await server.StartSessionAsync(
            "player-a",
            GameEndpointName.Control,
            "connection-a",
            new LoginCallback(),
            TestContext.Current.CancellationToken);
        await server.MarkEndpointDisconnectedAsync(
            session,
            GameEndpointName.Control,
            "connection-a",
            TestContext.Current.CancellationToken);

        var decision = await server.ResumeSessionAsync(
            new GameSessionResumeRequest(session),
            GameEndpointName.Control,
            "connection-b",
            new LoginCallback(),
            TestContext.Current.CancellationToken);

        Assert.Equal(SessionResumeStatus.Resumed, decision.Status);
        Assert.Equal(2, handler.EndpointBound.Count);
        Assert.Equal("connection-a", handler.EndpointBound[0].ConnectionId);
        Assert.Equal("connection-b", handler.EndpointBound[1].ConnectionId);
    }

    [Fact]
    public async Task RpcDisconnectMarksEndpointAggregateDisconnectedAndPublishesOnce()
    {
        var directory = new InMemoryGameSessionDirectory();
        var handler = new RecordingLifecycleHandler();
        var observer = new GameSessionRpcLifecycleObserver(
            directory,
            [handler],
            NullLogger<GameSessionRpcLifecycleObserver>.Instance);
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        var endpoint = new SessionEndpointKey(session, "control");

        await directory.BindEndpointAsync(endpoint, "connection-a", new LoginCallback(), TestContext.Current.CancellationToken);
        await directory.BindEndpointAsync(endpoint, "connection-a", new ChatCallback(), TestContext.Current.CancellationToken);

        await observer.OnSessionDisconnectedAsync(
            new RpcSessionLifecycleContext("connection-a", "connection-a"),
            error: null,
            TestContext.Current.CancellationToken);

        var disconnected = Assert.Single(handler.EndpointDisconnected);
        Assert.Equal(endpoint, disconnected.Endpoint);
        Assert.Equal("connection-a", disconnected.ConnectionId);
        Assert.Null(await directory.GetCallbackAsync<LoginCallback>(endpoint, TestContext.Current.CancellationToken));
        Assert.Null(await directory.GetCallbackAsync<ChatCallback>(endpoint, TestContext.Current.CancellationToken));
    }

    private sealed class RecordingLifecycleHandler : IGameSessionLifecycleHandler
    {
        public List<GameEndpointBindingContext> EndpointBound { get; } = [];

        public List<GameEndpointBindingContext> EndpointDisconnected { get; } = [];

        public ValueTask OnConnectionOpenedAsync(
            GameConnectionContext context,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnEndpointBoundAsync(
            GameEndpointBindingContext context,
            CancellationToken cancellationToken = default)
        {
            EndpointBound.Add(context);
            return default;
        }

        public ValueTask OnEndpointDisconnectedAsync(
            GameEndpointBindingContext context,
            CancellationToken cancellationToken = default)
        {
            EndpointDisconnected.Add(context);
            return default;
        }

        public ValueTask OnEndpointExpiredAsync(
            GameEndpointBindingContext context,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionTerminatedAsync(
            GameSessionTerminationContext context,
            CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    private sealed class LoginCallback
    {
    }

    private sealed class ChatCallback
    {
    }
}
