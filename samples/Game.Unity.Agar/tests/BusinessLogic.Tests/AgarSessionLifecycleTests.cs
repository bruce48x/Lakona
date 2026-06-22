using Lakona.Game.Abstractions;
using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Server.App.Hosting;
using Server.Hotfix.Services;
using Shared.Interfaces;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class AgarSessionLifecycleTests
{
    [Fact]
    public async Task RealtimeDisconnectOnBattleNodeDoesNotRequireControlPlaneServices()
    {
        var directory = CreateSessionDirectory();
        Assert.True(await AttachRealtimeAsync(
            directory,
            "player-1",
            "session-1",
            "room-1",
            "match-1",
            "realtime-1",
            new TestBattleCallback()));
        var services = BuildLifecycleServices(directory);
        await using var provider = services.BuildServiceProvider();

        var call = new HotfixLifecycleCall<GameSessionDisconnectedRequest>(
            new GameSessionDisconnectedRequest
            {
                OwnerKey = "player-1",
                ConnectionId = "realtime-1"
            },
            "realtime-1",
            provider,
            new ThrowingActorRuntime(),
            new TestGameServer());

        await AgarSessionLifecycle.SessionDisconnectedAsync(call);

        Assert.Null(GetRegistration(directory, "player-1"));
        Assert.Null(GetConnection(directory, "realtime-1"));
    }

    [Fact]
    public async Task ControlDisconnectClearsDirectoryWhenActorUpdateFails()
    {
        var directory = CreateSessionDirectory();
        await RegisterNewControlAsync(
            directory,
            "player-1",
            "session-1",
            "control-1");
        Assert.True(await BindControlCallbackAsync(
            directory,
            "player-1",
            "control-1",
            new TestControlCallback()));
        var services = BuildLifecycleServices(directory);
        await using var provider = services.BuildServiceProvider();

        var call = new HotfixLifecycleCall<GameSessionDisconnectedRequest>(
            new GameSessionDisconnectedRequest
            {
                OwnerKey = "player-1",
                ConnectionId = "control-1"
            },
            "control-1",
            provider,
            new ThrowingActorRuntime(),
            new TestGameServer());

        await AgarSessionLifecycle.SessionDisconnectedAsync(call);

        var registration = GetRegistration(directory, "player-1");
        Assert.NotNull(registration);
        var connectionId = Assert.IsType<string>(GetRequiredProperty(registration, "ConnectionId"));
        Assert.Empty(connectionId);
        Assert.Null(GetRequiredProperty(registration, "ControlCallback"));
        Assert.Null(GetConnection(directory, "control-1"));
    }

    private static ServiceCollection BuildLifecycleServices(object directory)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(SessionDirectoryType, directory);
        return services;
    }

    private static object CreateSessionDirectory()
    {
        return Activator.CreateInstance(SessionDirectoryType)
            ?? throw new InvalidOperationException("Could not create SessionDirectory.");
    }

    private static async ValueTask RegisterNewControlAsync(
        object directory,
        string playerId,
        string sessionToken,
        string connectionId)
    {
        var method = SessionDirectoryType.GetMethod("RegisterNewControlAsync")
            ?? throw new MissingMethodException(SessionDirectoryType.FullName, "RegisterNewControlAsync");
        _ = await (ValueTask<GameSessionKey>)method.Invoke(directory, [
            playerId,
            sessionToken,
            connectionId,
            TestContext.Current.CancellationToken
        ])!;
    }

    private static async ValueTask<bool> BindControlCallbackAsync(
        object directory,
        string playerId,
        string connectionId,
        IControlCallback callback)
    {
        var method = SessionDirectoryType.GetMethod("BindControlCallbackAsync")
            ?? throw new MissingMethodException(SessionDirectoryType.FullName, "BindControlCallbackAsync");
        return await (ValueTask<bool>)method.Invoke(directory, [
            playerId,
            connectionId,
            callback,
            TestContext.Current.CancellationToken
        ])!;
    }

    private static async ValueTask<bool> AttachRealtimeAsync(
        object directory,
        string playerId,
        string sessionToken,
        string roomId,
        string matchId,
        string connectionId,
        IBattleCallback callback)
    {
        var method = SessionDirectoryType.GetMethod("AttachRealtimeAsync")
            ?? throw new MissingMethodException(SessionDirectoryType.FullName, "AttachRealtimeAsync");
        return await (ValueTask<bool>)method.Invoke(directory, [
            playerId,
            sessionToken,
            roomId,
            matchId,
            connectionId,
            callback,
            TestContext.Current.CancellationToken
        ])!;
    }

    private static object? GetRegistration(object directory, string playerId)
    {
        var method = SessionDirectoryType.GetMethod("Get")
            ?? throw new MissingMethodException(SessionDirectoryType.FullName, "Get");
        return method.Invoke(directory, [playerId]);
    }

    private static object? GetConnection(object directory, string connectionId)
    {
        var method = SessionDirectoryType.GetMethod("GetConnection")
            ?? throw new MissingMethodException(SessionDirectoryType.FullName, "GetConnection");
        return method.Invoke(directory, [connectionId]);
    }

    private static object? GetRequiredProperty(object instance, string propertyName)
    {
        return instance.GetType().GetProperty(propertyName)?.GetValue(instance)
            ?? (instance.GetType().GetProperty(propertyName) is not null
                ? null
                : throw new MissingMemberException(instance.GetType().FullName, propertyName));
    }

    private static readonly Type SessionDirectoryType =
        typeof(AgarSampleServiceCollectionExtensions).Assembly.GetType("Server.App.Services.SessionDirectory")
        ?? throw new InvalidOperationException("Could not find Server.App.Services.SessionDirectory.");

    private sealed class ThrowingActorRuntime : IActorRuntime
    {
        public ValueTask<TActor> GetOrCreateAsync<TActor>(ActorId id, CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            throw new InvalidOperationException("Actor runtime should not be used by this test path.");
        }

        public ValueTask TellAsync<TActor>(
            ActorId id,
            Func<TActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            throw new InvalidOperationException("Actor runtime should not be used by this test path.");
        }

        public ActorTellResult TryTell<TActor>(
            ActorId id,
            Func<TActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            throw new InvalidOperationException("Actor runtime should not be used by this test path.");
        }

        public ValueTask TellAsync(
            Type actorType,
            ActorId id,
            Func<IActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Actor runtime should not be used by this test path.");
        }

        public ActorTellResult TryTell(
            Type actorType,
            ActorId id,
            Func<IActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Actor runtime should not be used by this test path.");
        }

        public ValueTask<TResult> AskAsync<TActor, TResult>(
            ActorId id,
            Func<TActor, CancellationToken, ValueTask<TResult>> message,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            throw new InvalidOperationException("Actor runtime failed.");
        }

        public IAsyncDisposable RegisterTimer<TActor>(
            ActorId id,
            TimeSpan dueTime,
            TimeSpan? period,
            Func<TActor, CancellationToken, ValueTask> callback)
            where TActor : class, IActor
        {
            throw new InvalidOperationException("Actor runtime should not be used by this test path.");
        }

        public bool TryGetMailboxMetrics(ActorId id, out ActorMailboxMetrics metrics)
        {
            metrics = default;
            return false;
        }

        public IReadOnlyList<ActorId> GetActiveActorIds(Type actorType)
        {
            throw new InvalidOperationException("Actor runtime should not be used by this test path.");
        }

        public ActorState GetState(ActorId id)
        {
            return ActorState.Dead;
        }

        public ValueTask StopAsync(ActorId id)
        {
            return default;
        }

        public ValueTask<ActorStopOutcome> StopAsync(ActorId id, TimeSpan drainTimeout)
        {
            return new ValueTask<ActorStopOutcome>(ActorStopOutcome.Drained);
        }
    }

    private sealed class TestGameServer : ILakonaGameServer
    {
        public ValueTask<GameSessionKey> StartSessionAsync(
            string ownerKey,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<GameSessionKey>(new GameSessionKey(ownerKey, "session", 1));
        }

        public ValueTask<GameSessionKey> StartSessionAsync<TCallback>(
            string ownerKey,
            string connectionId,
            TCallback callback,
            CancellationToken cancellationToken = default)
            where TCallback : class
        {
            return new ValueTask<GameSessionKey>(new GameSessionKey(ownerKey, "session", 1));
        }

        public ValueTask<SessionResumeDecision> ResumeSessionAsync<TCallback>(
            GameSessionResumeRequest request,
            string connectionId,
            TCallback callback,
            CancellationToken cancellationToken = default)
            where TCallback : class
        {
            return new ValueTask<SessionResumeDecision>(SessionResumeDecision.StateLost("Not used."));
        }

        public ValueTask BindSessionAsync<TCallback>(
            GameSessionKey session,
            string connectionId,
            TCallback callback,
            CancellationToken cancellationToken = default)
            where TCallback : class
        {
            return default;
        }

        public ValueTask BindCurrentSessionAsync<TCallback>(
            string connectionId,
            TCallback callback,
            CancellationToken cancellationToken = default)
            where TCallback : class
        {
            return default;
        }

        public ValueTask MarkSessionDisconnectedAsync(
            GameSessionKey session,
            string? connectionId = null,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask<TCallback?> GetCallbackAsync<TCallback>(
            GameSessionKey session,
            CancellationToken cancellationToken = default)
            where TCallback : class
        {
            return new ValueTask<TCallback?>((TCallback?)null);
        }

        public ValueTask TerminateSessionAsync(
            GameSessionKey session,
            SessionTerminationReason reason,
            string? message = null,
            SessionTerminationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask<long> PublishReliablePushAsync<TCallback, TPayload>(
            GameSessionKey session,
            string kind,
            TPayload payload,
            ReliablePushDeliver<TCallback, TPayload> deliver,
            CancellationToken cancellationToken = default)
            where TCallback : class
        {
            return new ValueTask<long>(0);
        }

        public ValueTask<long> PublishReliablePushAsync(
            GameSessionKey session,
            string kind,
            object payload,
            Func<ReliablePushRecord, ValueTask> deliver,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<long>(0);
        }

        public ValueTask ReplayReliablePushAsync(
            GameSessionKey session,
            Func<ReliablePushRecord, ValueTask> deliver,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask ReplayReliablePushAsync<TCallback, TPayload>(
            GameSessionKey session,
            string kind,
            ReliablePushDeliver<TCallback, TPayload> deliver,
            CancellationToken cancellationToken = default)
            where TCallback : class
        {
            return default;
        }

        public ValueTask<ReliablePushAckOutcome> AckReliablePushAsync(
            GameSessionKey currentSession,
            GameSessionKey acknowledgedSession,
            long sequence,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ReliablePushAckOutcome>(ReliablePushAckOutcome.StateLost());
        }
    }

    private sealed class TestControlCallback : IControlCallback
    {
        public void OnMatchmakingStatus(MatchmakingStatusUpdate matchmakingStatus)
        {
        }
    }

    private sealed class TestBattleCallback : IBattleCallback
    {
        public void OnWorldState(WorldState worldState)
        {
        }

        public void OnPlayerDead(PlayerDead deadEvent)
        {
        }

        public void OnMatchEnd(MatchEnd matchEnd)
        {
        }
    }
}
