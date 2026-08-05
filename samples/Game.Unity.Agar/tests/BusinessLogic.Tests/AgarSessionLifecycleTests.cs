using Server.App.Rooms;
using Server.App.Sessions;
using Server.App.Users;
using Lakona.Game.Abstractions;
using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Server.Hotfix.Matchmaking;
using Server.Hotfix.Rooms;
using Server.Hotfix.Sessions;
using Server.Hotfix.Users;
using Shared.Interfaces;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class AgarSessionLifecycleTests
{
    [Fact]
    public async Task RealtimeDisconnectDoesNotRequireControlPlaneActorServices()
    {
        await using var provider = BuildLifecycleServices(includeActors: false)
            .BuildReadyServiceProvider(TestContext.Current.CancellationToken);
        var call = new HotfixLifecycleCall<GameSessionDisconnectedRequest>(
            new GameSessionDisconnectedRequest
            {
                OwnerKey = "player-1",
                SessionId = "realtime-session",
                ConnectionId = "realtime-1"
            },
            "realtime-1",
            provider,
            new ThrowingActorRuntime(),
            new TestGameServer());

        await ActivatorUtilities.CreateInstance<AgarSessionLifecycle>(provider).SessionDisconnectedAsync(call);
    }

    [Fact]
    public async Task ControlDisconnectMarksUserActorFromRequestOwnerAndConnection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await TestHotfix.LoadCurrentAsync(cancellationToken);
        await using var provider = BuildLifecycleServices(includeActors: true)
            .BuildReadyServiceProvider(TestContext.Current.CancellationToken);
        var actors = provider.GetRequiredService<IActorRuntime>();
        await provider.GetRequiredService<ActorHosting>().EnsureAsync<UserActor>(ActorId.From("player-1"), cancellationToken);
        await LoginAndAttachUserAsync(
            actors,
            "player-1",
            "control-1",
            "control-session",
            cancellationToken);

        var call = new HotfixLifecycleCall<GameSessionDisconnectedRequest>(
            new GameSessionDisconnectedRequest
            {
                OwnerKey = "player-1",
                SessionId = "control-session",
                ConnectionId = "control-1"
            },
            "control-1",
            provider,
            actors,
            new TestGameServer());

        await ActivatorUtilities.CreateInstance<AgarSessionLifecycle>(provider).SessionDisconnectedAsync(call);

        var snapshot = await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From("player-1"),
            (actor, _) => actor.GetSnapshotAsync(new PlayerSessionSnapshotRequest()),
            cancellationToken);
        Assert.Equal("", snapshot.ConnectionId);
        Assert.Equal("control-session", snapshot.ControlSessionId);
    }

    [Fact]
    public async Task RealtimeDisconnectPreservesActorOwnedRealtimeStateForResume()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await TestHotfix.LoadCurrentAsync(cancellationToken);
        await using var provider = BuildLifecycleServices(includeActors: true)
            .BuildReadyServiceProvider(TestContext.Current.CancellationToken);
        var actors = provider.GetRequiredService<IActorRuntime>();
        var hosting = provider.GetRequiredService<ActorHosting>();
        await hosting.EnsureAsync<UserActor>(ActorId.From("player-1"), cancellationToken);
        await hosting.EnsureAsync<RoomActor>(ActorId.From("room-1"), cancellationToken);

        var login = await LoginAndAttachUserAsync(
            actors,
            "player-1",
            "control-1",
            "control-session",
            cancellationToken);
        await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From("player-1"),
            (actor, _) => actor.AssignRoomAsync(new PlayerRoomAssignment
            {
                UserId = "player-1",
                SessionToken = login.SessionToken,
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
                SessionToken = login.SessionToken,
                RoomId = "room-1",
                MatchId = "match-1",
                RealtimeSessionId = "realtime-session"
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
                        SessionToken = login.SessionToken,
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
                UpdatedAtUtc = DateTime.UtcNow
            }),
            cancellationToken);

        var call = new HotfixLifecycleCall<GameSessionDisconnectedRequest>(
            new GameSessionDisconnectedRequest
            {
                OwnerKey = "player-1",
                SessionId = "realtime-session",
                ConnectionId = "realtime-1"
            },
            "realtime-1",
            provider,
            actors,
            new TestGameServer());

        await ActivatorUtilities.CreateInstance<AgarSessionLifecycle>(provider).SessionDisconnectedAsync(call);

        var user = await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From("player-1"),
            (actor, _) => actor.GetSnapshotAsync(new PlayerSessionSnapshotRequest()),
            cancellationToken);
        var room = await actors.AskAsync<RoomActor, RoomSnapshot>(
            ActorId.From("room-1"),
            (actor, _) => actor.GetSnapshotAsync(new RoomSnapshotRequest()),
            cancellationToken);
        var roomPlayer = Assert.Single(room.Players);
        Assert.Equal("realtime-session", user.RealtimeSessionId);
        Assert.Equal("realtime-session", roomPlayer.RealtimeSessionId);
        Assert.True(roomPlayer.IsReady);
        Assert.True(roomPlayer.IsConnected);
    }

    [Fact]
    public async Task StaleControlSessionExpiryDoesNotReleaseNewerUserSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await TestHotfix.LoadCurrentAsync(cancellationToken);
        await using var provider = BuildLifecycleServices(includeActors: true)
            .BuildReadyServiceProvider(TestContext.Current.CancellationToken);
        var actors = provider.GetRequiredService<IActorRuntime>();
        await provider.GetRequiredService<ActorHosting>().EnsureAsync<UserActor>(ActorId.From("player-1"), cancellationToken);
        await LoginAndAttachUserAsync(
            actors,
            "player-1",
            "control-new",
            "control-session-new",
            cancellationToken);

        var call = new HotfixLifecycleCall<GameSessionExpiredRequest>(
            new GameSessionExpiredRequest
            {
                OwnerKey = "player-1",
                SessionId = "control-session-old",
                ConnectionId = "control-old"
            },
            "control-old",
            provider,
            actors,
            new TestGameServer());

        await ActivatorUtilities.CreateInstance<AgarSessionLifecycle>(provider).SessionExpiredAsync(call);

        var snapshot = await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From("player-1"),
            (actor, _) => actor.GetSnapshotAsync(new PlayerSessionSnapshotRequest()),
            cancellationToken);
        Assert.Equal("control-new", snapshot.ConnectionId);
        Assert.Equal("control-session-new", snapshot.ControlSessionId);
    }

    private static ValueTask<UserLoginResult> LoginAndAttachUserAsync(
        IActorRuntime actors,
        string userId,
        string connectionId,
        string controlSessionId,
        CancellationToken cancellationToken)
    {
        return actors.AskAsync<UserActor, UserLoginResult>(
            ActorId.From(userId),
            (actor, _) => actor.LoginAndAttachAsync(new UserLoginAndAttachRequest
            {
                Password = "pw",
                ConnectionId = connectionId,
                ControlSessionId = controlSessionId
            }),
            cancellationToken);
    }

    private static ServiceCollection BuildLifecycleServices(bool includeActors)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTestClusterRpc();
        services.AddLakonaGameServer();
        services.AddSingleton<MatchmakingNotifier>();
        if (includeActors)
        {
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
            return new ValueTask<GameSessionKey>(new GameSessionKey(ownerKey, "session"));
        }

        public ValueTask<GameSessionKey> StartSessionAsync(
            string ownerKey,
            string connectionId,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<GameSessionKey>(new GameSessionKey(ownerKey, "session"));
        }

        public ValueTask BindSessionAsync(
            GameSessionKey session,
            string connectionId,
            CancellationToken cancellationToken = default)
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
            return default;
        }

    }
}
