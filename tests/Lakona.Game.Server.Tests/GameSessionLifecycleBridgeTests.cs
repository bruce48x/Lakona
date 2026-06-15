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
    public async Task StartSessionPublishesSessionBoundOnceForActiveSession()
    {
        var handler = new RecordingLifecycleHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IGameSessionLifecycleHandler>(handler);
        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();

        var session = await server.StartSessionAsync(
            "player-a",
            "connection-a",
            new LoginCallback(),
            TestContext.Current.CancellationToken);
        await server.BindSessionAsync(
            session,
            "connection-a",
            new ChatCallback(),
            TestContext.Current.CancellationToken);
        await server.BindSessionAsync(
            session,
            "connection-b",
            new LoginCallback(),
            TestContext.Current.CancellationToken);

        var bound = Assert.Single(handler.SessionBound);
        Assert.Equal(session, bound.Session);
        Assert.Equal("connection-a", bound.ConnectionId);
    }

    [Fact]
    public async Task ResumeSessionPublishesSessionBoundWhenDisconnectedSessionBecomesActive()
    {
        var handler = new RecordingLifecycleHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IGameSessionLifecycleHandler>(handler);
        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();

        var session = await server.StartSessionAsync(
            "player-a",
            "connection-a",
            new LoginCallback(),
            TestContext.Current.CancellationToken);
        await server.MarkSessionDisconnectedAsync(
            session,
            "connection-a",
            TestContext.Current.CancellationToken);

        var decision = await server.ResumeSessionAsync(
            new GameSessionResumeRequest(session),
            "connection-b",
            new LoginCallback(),
            TestContext.Current.CancellationToken);

        Assert.Equal(SessionResumeStatus.Resumed, decision.Status);
        Assert.Equal(2, handler.SessionBound.Count);
        Assert.Equal("connection-a", handler.SessionBound[0].ConnectionId);
        Assert.Equal("connection-b", handler.SessionBound[1].ConnectionId);
    }

    [Fact]
    public async Task RpcDisconnectMarksSessionDisconnectedAndPublishesOnce()
    {
        var directory = new InMemoryGameSessionDirectory();
        var handler = new RecordingLifecycleHandler();
        var observer = new GameSessionRpcLifecycleObserver(
            directory,
            [handler],
            NullLogger<GameSessionRpcLifecycleObserver>.Instance);
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);

        await directory.BindSessionAsync(session, "connection-a", new LoginCallback(), TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "connection-a", new ChatCallback(), TestContext.Current.CancellationToken);

        await observer.OnSessionDisconnectedAsync(
            new RpcSessionLifecycleContext("connection-a", "connection-a"),
            error: null,
            TestContext.Current.CancellationToken);

        var disconnected = Assert.Single(handler.SessionDisconnected);
        Assert.Equal(session, disconnected.Session);
        Assert.Equal("connection-a", disconnected.ConnectionId);
        Assert.Null(await directory.GetCallbackAsync<LoginCallback>(session, TestContext.Current.CancellationToken));
        Assert.Null(await directory.GetCallbackAsync<ChatCallback>(session, TestContext.Current.CancellationToken));
    }

    private sealed class RecordingLifecycleHandler : IGameSessionLifecycleHandler
    {
        public List<GameSessionBindingContext> SessionBound { get; } = [];

        public List<GameSessionBindingContext> SessionDisconnected { get; } = [];

        public ValueTask OnConnectionOpenedAsync(
            GameConnectionContext context,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionBoundAsync(
            GameSessionBindingContext context,
            CancellationToken cancellationToken = default)
        {
            SessionBound.Add(context);
            return default;
        }

        public ValueTask OnSessionDisconnectedAsync(
            GameSessionBindingContext context,
            CancellationToken cancellationToken = default)
        {
            SessionDisconnected.Add(context);
            return default;
        }

        public ValueTask OnSessionExpiredAsync(
            GameSessionBindingContext context,
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
