using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Server;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class GameSessionLifecycleBridgeTests
{
    [Fact]
    public void AddSessionHotfixLifecycleDoesNotRequireGlobalContract()
    {
        var services = new ServiceCollection();
        services.AddLakonaGameSessionHotfixLifecycle();

        using var provider = services.BuildServiceProvider();

        var contracts = provider.GetServices<IHotfixRequiredServiceContracts>()
            .SelectMany(static item => item.ServiceContracts)
            .ToArray();
        Assert.DoesNotContain(typeof(IGameSessionLifecycle), contracts);
    }

    [Fact]
    public async Task SessionHotfixLifecycleNoopsWhenHotfixRuntimeIsMissing()
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
                new GameSessionKey("player-a", "session-a"),
                "connection-a"),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SessionHotfixLifecycleResolvesImplementationFromCurrentSnapshot()
    {
        var lifecycle = new RecordingLifecycle();
        var actorRuntime = new SnapshotActorRuntime();
        var gameServer = new SnapshotGameServer();
        using var snapshotServices = new ServiceCollection()
            .AddSingleton<IGameSessionLifecycle>(lifecycle)
            .AddSingleton<IActorRuntime>(actorRuntime)
            .AddSingleton<ILakonaGameServer>(gameServer)
            .BuildServiceProvider();
        var services = new ServiceCollection();
        services.AddSingleton<IHotfixRuntimeAccessor>(new FixedHotfixRuntimeAccessor(
            new HotfixRuntimeSnapshot(new ThrowingRpcInvoker(), snapshotServices)));
        services.AddLakonaGameSessionHotfixLifecycle();

        using var provider = services.BuildServiceProvider();
        var bindings = provider.GetRequiredService<GameSessionLifecycleBindings>();
        var identity = GameSessionLifecycleBindings.Identity(lifecycle.GetType());
        bindings.Publish([identity], () => { });
        bindings.Bind(new GameSessionKey("player-a", "session-a"), identity);
        var handler = provider.GetServices<IGameSessionLifecycleHandler>()
            .OfType<GameSessionHotfixLifecycleHandler>()
            .Single();

        await handler.OnSessionExpiredAsync(
            new GameSessionBindingContext(
                new GameSessionKey("player-a", "session-a"),
                "connection-a"),
            TestContext.Current.CancellationToken);

        var call = Assert.IsType<HotfixLifecycleCall<GameSessionExpiredRequest>>(lifecycle.Argument);
        Assert.Equal("session-a", call.Request.SessionId);
    }

    [Fact]
    public async Task SessionHotfixLifecycleHoldsRuntimeLeaseUntilExpiredInvocationCompletes()
    {
        var lifecycle = new BlockingLifecycle();
        var actorRuntime = new SnapshotActorRuntime();
        var gameServer = new SnapshotGameServer();
        var innerServices = new ServiceCollection()
            .AddSingleton<IGameSessionLifecycle>(lifecycle)
            .AddSingleton<IActorRuntime>(actorRuntime)
            .AddSingleton<ILakonaGameServer>(gameServer)
            .BuildServiceProvider();
        var snapshotServices = new TrackingServiceProvider(innerServices);
        var snapshot = new HotfixRuntimeSnapshot(
            new ThrowingRpcInvoker(),
            snapshotServices,
            onRetired: snapshotServices.Dispose);
        var services = new ServiceCollection();
        services.AddSingleton<IHotfixRuntimeAccessor>(new FixedHotfixRuntimeAccessor(snapshot));
        services.AddLakonaGameSessionHotfixLifecycle();

        using var provider = services.BuildServiceProvider();
        var bindings = provider.GetRequiredService<GameSessionLifecycleBindings>();
        var identity = GameSessionLifecycleBindings.Identity(lifecycle.GetType());
        bindings.Publish([identity], () => { });
        bindings.Bind(new GameSessionKey("player-a", "session-a"), identity);
        var handler = provider.GetServices<IGameSessionLifecycleHandler>()
            .OfType<GameSessionHotfixLifecycleHandler>()
            .Single();

        var expired = handler.OnSessionExpiredAsync(
            new GameSessionBindingContext(
                new GameSessionKey("player-a", "session-a"),
                "connection-a"),
            TestContext.Current.CancellationToken).AsTask();
        await lifecycle.Invoked.Task.WaitAsync(TestContext.Current.CancellationToken);

        snapshot.Retire();
        Assert.False(snapshotServices.Disposed);

        lifecycle.Release.SetResult();
        await expired;

        Assert.True(snapshotServices.Disposed);
    }

    [Fact]
    public async Task SessionHotfixLifecycleHoldsRuntimeLeaseUntilResumedInvocationCompletes()
    {
        var lifecycle = new BlockingLifecycle();
        var actorRuntime = new SnapshotActorRuntime();
        var gameServer = new SnapshotGameServer();
        var innerServices = new ServiceCollection()
            .AddSingleton<IGameSessionLifecycle>(lifecycle)
            .AddSingleton<IActorRuntime>(actorRuntime)
            .AddSingleton<ILakonaGameServer>(gameServer)
            .BuildServiceProvider();
        var snapshotServices = new TrackingServiceProvider(innerServices);
        var snapshot = new HotfixRuntimeSnapshot(
            new ThrowingRpcInvoker(),
            snapshotServices,
            onRetired: snapshotServices.Dispose);
        var services = new ServiceCollection();
        services.AddSingleton<IHotfixRuntimeAccessor>(new FixedHotfixRuntimeAccessor(snapshot));
        services.AddLakonaGameSessionHotfixLifecycle();

        using var provider = services.BuildServiceProvider();
        var bindings = provider.GetRequiredService<GameSessionLifecycleBindings>();
        var identity = GameSessionLifecycleBindings.Identity(lifecycle.GetType());
        bindings.Publish([identity], () => { });
        bindings.Bind(new GameSessionKey("player-a", "session-a"), identity);
        var handler = provider.GetServices<IGameSessionLifecycleHandler>()
            .OfType<GameSessionHotfixLifecycleHandler>()
            .Single();

        var resumed = handler.OnSessionResumedAsync(
            new GameSessionBindingContext(
                new GameSessionKey("player-a", "session-a"),
                "connection-a"),
            TestContext.Current.CancellationToken).AsTask();
        await lifecycle.Invoked.Task.WaitAsync(TestContext.Current.CancellationToken);

        snapshot.Retire();
        Assert.False(snapshotServices.Disposed);

        lifecycle.Release.SetResult();
        await resumed;

        Assert.True(snapshotServices.Disposed);
    }

    [Fact]
    public async Task SessionHotfixLifecycleDispatchesExpiredSessionThroughFrameworkContract()
    {
        var lifecycle = new RecordingLifecycle();
        var services = new ServiceCollection();
        services.AddSingleton<IGameSessionLifecycle>(lifecycle);
        services.AddLakonaGameServer();
        services.AddSingleton<IHotfixRuntimeAccessor>(provider =>
            new FixedHotfixRuntimeAccessor(new HotfixRuntimeSnapshot(new ThrowingRpcInvoker(), provider)));
        services.AddLakonaGameSessionHotfixLifecycle();

        using var provider = services.BuildServiceProvider();
        var bindings = provider.GetRequiredService<GameSessionLifecycleBindings>();
        var identity = GameSessionLifecycleBindings.Identity(lifecycle.GetType());
        bindings.Publish([identity], () => { });
        bindings.Bind(new GameSessionKey("player-a", "session-a"), identity);
        var handler = provider.GetServices<IGameSessionLifecycleHandler>()
            .OfType<GameSessionHotfixLifecycleHandler>()
            .Single();

        await handler.OnSessionExpiredAsync(
            new GameSessionBindingContext(
                new GameSessionKey("player-a", "session-a"),
                "connection-a"),
            TestContext.Current.CancellationToken);


        var call = Assert.IsType<HotfixLifecycleCall<GameSessionExpiredRequest>>(lifecycle.Argument);
        Assert.Equal("player-a", call.Request.OwnerKey);
        Assert.Equal("session-a", call.Request.SessionId);
        Assert.Equal("connection-a", call.Request.ConnectionId);
    }

    [Fact]
    public async Task SessionHotfixLifecycleDispatchesDisconnectedSessionThroughFrameworkContract()
    {
        var lifecycle = new RecordingLifecycle();
        var services = new ServiceCollection();
        services.AddSingleton<IGameSessionLifecycle>(lifecycle);
        services.AddLakonaGameServer();
        services.AddSingleton<IHotfixRuntimeAccessor>(provider =>
            new FixedHotfixRuntimeAccessor(new HotfixRuntimeSnapshot(new ThrowingRpcInvoker(), provider)));
        services.AddLakonaGameSessionHotfixLifecycle();

        using var provider = services.BuildServiceProvider();
        var bindings = provider.GetRequiredService<GameSessionLifecycleBindings>();
        var identity = GameSessionLifecycleBindings.Identity(lifecycle.GetType());
        bindings.Publish([identity], () => { });
        bindings.Bind(new GameSessionKey("player-a", "session-a"), identity);
        var handler = provider.GetServices<IGameSessionLifecycleHandler>()
            .OfType<GameSessionHotfixLifecycleHandler>()
            .Single();

        await handler.OnSessionDisconnectedAsync(
            new GameSessionBindingContext(
                new GameSessionKey("player-a", "session-a"),
                "connection-a"),
            TestContext.Current.CancellationToken);


        var call = Assert.IsType<HotfixLifecycleCall<GameSessionDisconnectedRequest>>(lifecycle.Argument);
        Assert.Equal("player-a", call.Request.OwnerKey);
        Assert.Equal("session-a", call.Request.SessionId);
        Assert.Equal("connection-a", call.Request.ConnectionId);
    }

    [Fact]
    public async Task SessionHotfixLifecycleDispatchesResumedSessionThroughFrameworkContract()
    {
        var lifecycle = new RecordingLifecycle();
        var services = new ServiceCollection();
        services.AddSingleton<IGameSessionLifecycle>(lifecycle);
        services.AddLakonaGameServer();
        services.AddSingleton<IHotfixRuntimeAccessor>(provider =>
            new FixedHotfixRuntimeAccessor(new HotfixRuntimeSnapshot(new ThrowingRpcInvoker(), provider)));
        services.AddLakonaGameSessionHotfixLifecycle();

        using var provider = services.BuildServiceProvider();
        var bindings = provider.GetRequiredService<GameSessionLifecycleBindings>();
        var identity = GameSessionLifecycleBindings.Identity(lifecycle.GetType());
        bindings.Publish([identity], () => { });
        bindings.Bind(new GameSessionKey("player-a", "session-a"), identity);
        var handler = provider.GetServices<IGameSessionLifecycleHandler>()
            .OfType<GameSessionHotfixLifecycleHandler>()
            .Single();

        await handler.OnSessionResumedAsync(
            new GameSessionBindingContext(
                new GameSessionKey("player-a", "session-a"),
                "connection-a"),
            TestContext.Current.CancellationToken);


        var call = Assert.IsType<HotfixLifecycleCall<GameSessionResumedRequest>>(lifecycle.Argument);
        Assert.Equal("player-a", call.Request.OwnerKey);
        Assert.Equal("session-a", call.Request.SessionId);
        Assert.Equal("connection-a", call.Request.ConnectionId);
    }

    [Fact]
    public async Task StartSessionPublishesSessionBoundOnceForActiveSession()
    {
        var handler = new RecordingLifecycleHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IGameSessionLifecycleHandler>(handler);
        services.AddSingleton<IGameSessionEstablishedNotifier, NoopGameSessionEstablishedNotifier>();
        services.AddLakonaGameServer();
        services.UseReadySingleNodeMembership();
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();

        var session = await server.StartSessionAsync(
            "player-a",
            "connection-a",
            TestContext.Current.CancellationToken);
        await server.BindSessionAsync(
            session,
            "connection-a",
            TestContext.Current.CancellationToken);
        await server.BindSessionAsync(
            session,
            "connection-b",
            TestContext.Current.CancellationToken);

        var bound = Assert.Single(handler.SessionBound);
        Assert.Equal(session, bound.Session);
        Assert.Equal("connection-a", bound.ConnectionId);
    }

    [Fact]
    public async Task RpcDisconnectMarksSessionDisconnectedAndPublishesOnce()
    {
        var directory = new InMemoryGameSessionRegistry();
        var handler = new RecordingLifecycleHandler();
        var observer = new GameSessionRpcLifecycleObserver(
            directory,
            [handler],
            NullLogger<GameSessionRpcLifecycleObserver>.Instance);
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);

        await directory.BindSessionAsync(session, "connection-a", TestContext.Current.CancellationToken);

        await observer.OnSessionDisconnectedAsync(
            new RpcSessionLifecycleContext("connection-a", "connection-a"),
            error: null,
            TestContext.Current.CancellationToken);

        var disconnected = Assert.Single(handler.SessionDisconnected);
        Assert.Equal(session, disconnected.Session);
        Assert.Equal("connection-a", disconnected.ConnectionId);
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

    private sealed class FixedHotfixRuntimeAccessor : IHotfixRuntimeAccessor
    {
        public FixedHotfixRuntimeAccessor(HotfixRuntimeSnapshot current)
        {
            Current = current;
        }

        public HotfixRuntimeSnapshot Current { get; }
    }

    private sealed class TrackingServiceProvider(IServiceProvider inner) : IServiceProvider, IDisposable
    {
        public bool Disposed { get; private set; }

        public object? GetService(Type serviceType)
        {
            return inner.GetService(serviceType);
        }

        public void Dispose()
        {
            Disposed = true;
            (inner as IDisposable)?.Dispose();
        }
    }

    private sealed class SnapshotActorRuntime : IActorRuntime
    {
        public ValueTask TellAsync<TActor>(
            ActorId id,
            Func<TActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            throw new NotSupportedException();
        }

        public ActorTellResult TryTell<TActor>(
            ActorId id,
            Func<TActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            throw new NotSupportedException();
        }

        public ValueTask TellAsync(
            Type actorType,
            ActorId id,
            Func<IActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ActorTellResult TryTell(
            Type actorType,
            ActorId id,
            Func<IActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<TResult> AskAsync<TActor, TResult>(
            ActorId id,
            Func<TActor, CancellationToken, ValueTask<TResult>> message,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            throw new NotSupportedException();
        }

        public IReadOnlyList<ActorId> GetActiveActorIds(Type actorType)
        {
            throw new NotSupportedException();
        }

        public bool TryGetMailboxMetrics(ActorId id, out ActorMailboxMetrics metrics)
        {
            metrics = default;
            return false;
        }

        public ActorState GetState(ActorId id)
        {
            throw new NotSupportedException();
        }

    }

    private sealed class SnapshotGameServer : ILakonaGameServer
    {
        public ValueTask<GameSessionKey> StartSessionAsync<TLifecycle>(string ownerKey, CancellationToken cancellationToken = default)
            where TLifecycle : class, Lakona.Game.Server.Hotfix.IGameSessionLifecycle => StartSessionAsync(ownerKey, cancellationToken);
        public ValueTask<GameSessionKey> StartSessionAsync<TLifecycle>(string ownerKey, string connectionId, CancellationToken cancellationToken = default)
            where TLifecycle : class, Lakona.Game.Server.Hotfix.IGameSessionLifecycle => StartSessionAsync(ownerKey, connectionId, cancellationToken);

        public ValueTask<GameSessionKey> StartSessionAsync(
            string ownerKey,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<GameSessionKey> StartSessionAsync(
            string ownerKey,
            string connectionId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask BindSessionAsync(
            GameSessionKey session,
            string connectionId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask MarkSessionDisconnectedAsync(
            GameSessionKey session,
            string? connectionId = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask SetSessionItemAsync(
            GameSessionKey session,
            string key,
            GameSessionItemValue value,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask<GameSessionItemValue?> GetSessionItemAsync(
            GameSessionKey session,
            string key,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<GameSessionItemValue?>((GameSessionItemValue?)null);
        }

        public ValueTask<GameSessionItems> GetSessionItemsAsync(
            GameSessionKey session,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<GameSessionItems>(GameSessionItems.Empty);
        }

        public ValueTask RemoveSessionItemAsync(
            GameSessionKey session,
            string key,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask TerminateSessionAsync(
            GameSessionKey session,
            SessionTerminationReason reason,
            string? message = null,
            SessionTerminationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

    }

    [HotfixLifecycle]
    private sealed class RecordingLifecycle : IGameSessionLifecycle
    {
        public object? Argument { get; private set; }
        public ValueTask SessionResumedAsync(HotfixLifecycleCall<GameSessionResumedRequest> call)
        {
            Argument = call;
            return default;
        }

        public ValueTask SessionDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call)
        {
            Argument = call;
            return default;
        }
        public ValueTask SessionExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call)
        {
            Argument = call;
            return default;
        }
    }

    [HotfixLifecycle]
    private sealed class BlockingLifecycle : IGameSessionLifecycle
    {
        public TaskCompletionSource Invoked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask SessionResumedAsync(HotfixLifecycleCall<GameSessionResumedRequest> call)
        {
            Invoked.SetResult();
            await Release.Task.ConfigureAwait(false);
        }

        public ValueTask SessionDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call) => default;
        public async ValueTask SessionExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call)
        {
            Invoked.SetResult();
            await Release.Task.ConfigureAwait(false);
        }
    }

    private sealed class ThrowingRpcInvoker : IHotfixServiceInvoker
    {
        public ValueTask<TResult> InvokeHttpAsync<TArg, TResult>(int endpointSlot, TArg arg, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Lifecycle callbacks must not use RPC dispatch.");
        public ValueTask InvokeAsync<TContract, TArg>(int methodId, TArg arg, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Lifecycle callbacks must not use RPC dispatch.");
        public ValueTask<TResult> InvokeAsync<TContract, TArg, TResult>(int methodId, TArg arg, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Lifecycle callbacks must not use RPC dispatch.");
    }
}