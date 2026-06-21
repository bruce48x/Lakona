using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Server;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class GameSessionLifecycleBridgeTests
{
    [Fact]
    public void AddSessionHotfixLifecycleRegistersRequiredContract()
    {
        var services = new ServiceCollection();
        services.AddLakonaGameSessionHotfixLifecycle();

        using var provider = services.BuildServiceProvider();

        var contracts = provider.GetServices<IHotfixRequiredServiceContracts>()
            .SelectMany(static item => item.ServiceContracts)
            .ToArray();
        Assert.Contains(typeof(IGameSessionLifecycle), contracts);
    }

    [Fact]
    public async Task SessionHotfixLifecycleNoopsWhenHotfixInvokerIsMissing()
    {
        var services = new ServiceCollection();
        services.AddLakonaGameServer();
        services.AddLakonaGameSessionHotfixLifecycle();

        using var provider = services.BuildServiceProvider();
        var handler = provider.GetServices<IGameSessionLifecycleHandler>()
            .OfType<GameSessionHotfixLifecycleHandler>()
            .Single();

        await handler.OnSessionExpiredAsync(
            new GameSessionBindingContext(
                new GameSessionKey("player-a", "session-a", 1),
                "connection-a",
                [typeof(LoginCallback)]),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SessionHotfixLifecycleDispatchesExpiredSessionThroughFrameworkContract()
    {
        var invoker = new RecordingHotfixServiceInvoker();
        var services = new ServiceCollection();
        services.AddLakonaGameServer();
        services.AddSingleton<IHotfixServiceInvoker>(invoker);
        services.AddLakonaGameSessionHotfixLifecycle();

        using var provider = services.BuildServiceProvider();
        var handler = provider.GetServices<IGameSessionLifecycleHandler>()
            .OfType<GameSessionHotfixLifecycleHandler>()
            .Single();

        await handler.OnSessionExpiredAsync(
            new GameSessionBindingContext(
                new GameSessionKey("player-a", "session-a", 3),
                "connection-a",
                [typeof(LoginCallback), typeof(ChatCallback)]),
            TestContext.Current.CancellationToken);

        Assert.Equal(typeof(IGameSessionLifecycle), invoker.ContractType);
        Assert.Equal(typeof(HotfixLifecycleCall<GameSessionExpiredRequest>), invoker.ArgumentType);
        Assert.Equal(GameSessionLifecycleMethodIds.SessionExpired, invoker.MethodId);

        var call = Assert.IsType<HotfixLifecycleCall<GameSessionExpiredRequest>>(invoker.Argument);
        Assert.Equal("player-a", call.Request.OwnerKey);
        Assert.Equal("session-a", call.Request.SessionId);
        Assert.Equal(3, call.Request.Generation);
        Assert.Equal("connection-a", call.Request.ConnectionId);
        Assert.Equal(
            [
                typeof(LoginCallback).FullName ?? typeof(LoginCallback).Name,
                typeof(ChatCallback).FullName ?? typeof(ChatCallback).Name
            ],
            call.Request.CallbackContractTypeNames);
    }

    [Fact]
    public async Task SessionHotfixLifecycleDispatchesDisconnectedSessionThroughFrameworkContract()
    {
        var invoker = new RecordingHotfixServiceInvoker();
        var services = new ServiceCollection();
        services.AddLakonaGameServer();
        services.AddSingleton<IHotfixServiceInvoker>(invoker);
        services.AddLakonaGameSessionHotfixLifecycle();

        using var provider = services.BuildServiceProvider();
        var handler = provider.GetServices<IGameSessionLifecycleHandler>()
            .OfType<GameSessionHotfixLifecycleHandler>()
            .Single();

        await handler.OnSessionDisconnectedAsync(
            new GameSessionBindingContext(
                new GameSessionKey("player-a", "session-a", 3),
                "connection-a",
                [typeof(LoginCallback), typeof(ChatCallback)]),
            TestContext.Current.CancellationToken);

        Assert.Equal(typeof(IGameSessionLifecycle), invoker.ContractType);
        Assert.Equal(typeof(HotfixLifecycleCall<GameSessionDisconnectedRequest>), invoker.ArgumentType);
        Assert.Equal(GameSessionLifecycleMethodIds.SessionDisconnected, invoker.MethodId);

        var call = Assert.IsType<HotfixLifecycleCall<GameSessionDisconnectedRequest>>(invoker.Argument);
        Assert.Equal("player-a", call.Request.OwnerKey);
        Assert.Equal("session-a", call.Request.SessionId);
        Assert.Equal(3, call.Request.Generation);
        Assert.Equal("connection-a", call.Request.ConnectionId);
        Assert.Equal(
            [
                typeof(LoginCallback).FullName ?? typeof(LoginCallback).Name,
                typeof(ChatCallback).FullName ?? typeof(ChatCallback).Name
            ],
            call.Request.CallbackContractTypeNames);
    }

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

    private sealed class RecordingHotfixServiceInvoker : IHotfixServiceInvoker
    {
        public Type? ContractType { get; private set; }

        public Type? ArgumentType { get; private set; }

        public int MethodId { get; private set; }

        public object? Argument { get; private set; }

        public ValueTask InvokeAsync<TContract, TArg>(
            int methodId,
            TArg arg,
            CancellationToken cancellationToken = default)
        {
            ContractType = typeof(TContract);
            ArgumentType = typeof(TArg);
            MethodId = methodId;
            Argument = arg;
            return default;
        }

        public ValueTask<TResult> InvokeAsync<TContract, TArg, TResult>(
            int methodId,
            TArg arg,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask InvokeAsync<TContract, TArg>(
            string methodName,
            TArg arg,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<TResult> InvokeAsync<TContract, TArg, TResult>(
            string methodName,
            TArg arg,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
