using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Contracts.Users;
using Agar.Sample.State.Rooms;
using Agar.Sample.State.Users;
using Lakona.Game.Abstractions;
using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Server.Hotfix.Services;
using Server.Hotfix.State.Rooms;
using Server.Hotfix.State.Users;
using Shared.Interfaces;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class AgarSessionLifecycleTests
{
    [Fact]
    public async Task RealtimeDisconnectDoesNotRequireControlPlaneActorServices()
    {
        await using var provider = BuildLifecycleServices(includeActors: false).BuildServiceProvider();
        var call = new HotfixLifecycleCall<GameSessionDisconnectedRequest>(
            new GameSessionDisconnectedRequest
            {
                OwnerKey = "player-1",
                SessionId = "realtime-session",
                Generation = 1,
                ConnectionId = "realtime-1",
                CallbackContractTypeNames = [typeof(IBattleCallback).FullName!]
            },
            "realtime-1",
            provider,
            new ThrowingActorRuntime(),
            new TestGameServer());

        await AgarSessionLifecycle.SessionDisconnectedAsync(call);
    }

    [Fact]
    public async Task ControlDisconnectMarksUserActorFromRequestOwnerAndConnection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await TestHotfix.LoadCurrentAsync(cancellationToken);
        await using var provider = BuildLifecycleServices(includeActors: true).BuildServiceProvider();
        var actors = provider.GetRequiredService<IActorRuntime>();
        await provider.GetRequiredService<ActorHosting>().EnsureAsync<UserActor>(ActorId.From("player-1"), cancellationToken);
        await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From("player-1"),
            (actor, _) => actor.AttachAsync(new PlayerSessionAttachRequest
            {
                UserId = "player-1",
                SessionToken = "token-1",
                ConnectionId = "control-1",
                ControlSessionId = "control-session",
                ControlSessionGeneration = 1,
                AttachedAtUtc = DateTime.UtcNow
            }),
            cancellationToken);

        var call = new HotfixLifecycleCall<GameSessionDisconnectedRequest>(
            new GameSessionDisconnectedRequest
            {
                OwnerKey = "player-1",
                SessionId = "control-session",
                Generation = 1,
                ConnectionId = "control-1",
                CallbackContractTypeNames = [typeof(ILoginCallback).FullName!]
            },
            "control-1",
            provider,
            actors,
            new TestGameServer());

        await AgarSessionLifecycle.SessionDisconnectedAsync(call);

        var snapshot = await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From("player-1"),
            (actor, _) => actor.GetSnapshotAsync(new PlayerSessionSnapshotRequest()),
            cancellationToken);
        Assert.False(snapshot.IsOnline);
        Assert.Equal("", snapshot.ConnectionId);
        Assert.Equal("control-session", snapshot.ControlSessionId);
        Assert.Equal(1, snapshot.ControlSessionGeneration);
    }

    [Fact]
    public async Task RealtimeDisconnectClearsExactActorOwnedRealtimeState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await TestHotfix.LoadCurrentAsync(cancellationToken);
        await using var provider = BuildLifecycleServices(includeActors: true).BuildServiceProvider();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var hosting = provider.GetRequiredService<ActorHosting>();
        await hosting.EnsureAsync<UserActor>(ActorId.From("player-1"), cancellationToken);
        await hosting.EnsureAsync<RoomActor>(ActorId.From("room-1"), cancellationToken);

        await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From("player-1"),
            (actor, _) => actor.AttachAsync(new PlayerSessionAttachRequest
            {
                UserId = "player-1",
                SessionToken = "token-1",
                ConnectionId = "control-1",
                ControlSessionId = "control-session",
                ControlSessionGeneration = 1,
                AttachedAtUtc = DateTime.UtcNow
            }),
            cancellationToken);
        await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From("player-1"),
            (actor, _) => actor.AssignRoomAsync(new PlayerRoomAssignment
            {
                UserId = "player-1",
                SessionToken = "token-1",
                RoomId = "room-1",
                MatchId = "match-1",
                SeatIndex = 0,
                AssignedAtUtc = DateTime.UtcNow
            }),
            cancellationToken);
        await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From("player-1"),
            (actor, _) => actor.AttachRealtimeAsync(new PlayerRealtimeAttachRequest
            {
                UserId = "player-1",
                SessionToken = "token-1",
                RoomId = "room-1",
                MatchId = "match-1",
                RealtimeSessionId = "realtime-session",
                RealtimeSessionGeneration = 1,
                AttachedAtUtc = DateTime.UtcNow
            }),
            cancellationToken);
        await actors.AskAsync<RoomActor, RoomSettlementResult>(
            ActorId.From("room-1"),
            (actor, _) => actor.CreateAsync(new RoomCreateRequest
            {
                RoomId = "room-1",
                MatchId = "match-1",
                CreatedByUserId = "player-1",
                CreatedAtUtc = DateTime.UtcNow,
                Players =
                [
                    new PlayerRoomAssignment
                    {
                        UserId = "player-1",
                        SessionToken = "token-1",
                        ConnectionId = "control-1",
                        RoomId = "room-1",
                        MatchId = "match-1",
                        SeatIndex = 0,
                        AssignedAtUtc = DateTime.UtcNow
                    }
                ]
            }),
            cancellationToken);
        await actors.AskAsync<RoomActor, RoomSettlementResult>(
            ActorId.From("room-1"),
            (actor, _) => actor.SetReadyAsync(new RoomPlayerReadyRequest
            {
                UserId = "player-1",
                RoomId = "room-1",
                IsReady = true,
                RealtimeSessionId = "realtime-session",
                RealtimeSessionGeneration = 1,
                UpdatedAtUtc = DateTime.UtcNow
            }),
            cancellationToken);

        var call = new HotfixLifecycleCall<GameSessionDisconnectedRequest>(
            new GameSessionDisconnectedRequest
            {
                OwnerKey = "player-1",
                SessionId = "realtime-session",
                Generation = 1,
                ConnectionId = "realtime-1",
                CallbackContractTypeNames = [typeof(IBattleCallback).FullName!]
            },
            "realtime-1",
            provider,
            actors,
            new TestGameServer());

        await AgarSessionLifecycle.SessionDisconnectedAsync(call);

        var user = await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From("player-1"),
            (actor, _) => actor.GetSnapshotAsync(new PlayerSessionSnapshotRequest()),
            cancellationToken);
        var room = await actors.AskAsync<RoomActor, RoomSnapshot>(
            ActorId.From("room-1"),
            (actor, _) => actor.GetSnapshotAsync(new RoomSnapshotRequest()),
            cancellationToken);
        var roomPlayer = Assert.Single(room.Players);
        Assert.Equal("", user.RealtimeSessionId);
        Assert.Equal(0, user.RealtimeSessionGeneration);
        Assert.Equal("", roomPlayer.RealtimeSessionId);
        Assert.Equal(0, roomPlayer.RealtimeSessionGeneration);
        Assert.False(roomPlayer.IsReady);
        Assert.False(roomPlayer.IsConnected);
    }

    [Fact]
    public async Task StaleControlSessionExpiryDoesNotReleaseNewerUserSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await TestHotfix.LoadCurrentAsync(cancellationToken);
        await using var provider = BuildLifecycleServices(includeActors: true).BuildServiceProvider();
        var actors = provider.GetRequiredService<IActorRuntime>();
        await provider.GetRequiredService<ActorHosting>().EnsureAsync<UserActor>(ActorId.From("player-1"), cancellationToken);
        await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From("player-1"),
            (actor, _) => actor.AttachAsync(new PlayerSessionAttachRequest
            {
                UserId = "player-1",
                SessionToken = "token-new",
                ConnectionId = "control-new",
                ControlSessionId = "control-session-new",
                ControlSessionGeneration = 2,
                AttachedAtUtc = DateTime.UtcNow
            }),
            cancellationToken);

        var call = new HotfixLifecycleCall<GameSessionExpiredRequest>(
            new GameSessionExpiredRequest
            {
                OwnerKey = "player-1",
                SessionId = "control-session-old",
                Generation = 1,
                ConnectionId = "control-old",
                CallbackContractTypeNames = [typeof(ILoginCallback).FullName!]
            },
            "control-old",
            provider,
            actors,
            new TestGameServer());

        await AgarSessionLifecycle.SessionExpiredAsync(call);

        var snapshot = await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From("player-1"),
            (actor, _) => actor.GetSnapshotAsync(new PlayerSessionSnapshotRequest()),
            cancellationToken);
        Assert.True(snapshot.IsOnline);
        Assert.Equal("control-new", snapshot.ConnectionId);
        Assert.Equal("control-session-new", snapshot.ControlSessionId);
        Assert.Equal(2, snapshot.ControlSessionGeneration);
    }

    private static ServiceCollection BuildLifecycleServices(bool includeActors)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (includeActors)
        {
            services.AddLakonaGameServer();
            services.AddGeneratedActorSelectorTestDependencies();
        }

        return services;
    }

    private sealed class ThrowingActorRuntime : IActorRuntime
    {
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

    }
}
