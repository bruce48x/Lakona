using System.Reflection;
using Server.App.Routing;
using Server.App.Rooms;
using Server.App.Sessions;
using Server.App.Users;
using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Microsoft.Extensions.DependencyInjection;
using Server.Hotfix.Rooms;
using Server.Hotfix.Users;
using Shared.Gameplay;
using Shared.Interfaces;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class PlayerSessionActorStateTests
{
    private const string RealtimeInputSessionId = "realtime-session-1";

    [Fact]
    public async Task UserActorLoginAndAttachUpdatesAccountAndControlSessionInOneTurn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateActorServices();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var userId = "player-login-and-attach";

        await provider.GetRequiredService<ActorHosting>().EnsureAsync<UserActor>(ActorId.From(userId), cancellationToken);

        var login = await actors.AskAsync<UserActor, UserLoginResult>(
            ActorId.From(userId),
            (actor, _) => actor.LoginAndAttachAsync(new UserLoginAndAttachRequest
            {
                Password = "pw",
                ConnectionId = "control-connection-1",
                ControlSessionId = "control-session-1"
            }),
            cancellationToken);
        var session = await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From(userId),
            (actor, _) => actor.GetSnapshotAsync(new PlayerSessionSnapshotRequest()),
            cancellationToken);

        Assert.Equal(userId, login.UserId);
        Assert.False(string.IsNullOrWhiteSpace(login.SessionToken));
        Assert.Equal(login.SessionToken, session.SessionToken);
        Assert.Equal("control-connection-1", session.ConnectionId);
        Assert.Equal("control-session-1", session.ControlSessionId);
    }

    [Fact]
    public async Task UserActorPersistsControlAndRealtimeFrameworkSessionMetadata()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateActorServices();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var userId = "player-session-metadata";

        await provider.GetRequiredService<ActorHosting>().EnsureAsync<UserActor>(ActorId.From(userId), cancellationToken);

        var login = await LoginAndAttachUserAsync(
            actors,
            userId,
            "control-connection-1",
            "control-session-1",
            cancellationToken);
        var attached = await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From(userId),
            (actor, _) => actor.GetSnapshotAsync(new PlayerSessionSnapshotRequest()),
            cancellationToken);

        Assert.Equal("control-session-1", attached.ControlSessionId);
        Assert.Equal("", attached.RealtimeSessionId);

        await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From(userId),
            (actor, _) => actor.AssignRoomAsync(new PlayerRoomAssignment
            {
                UserId = userId,
                SessionToken = login.SessionToken,
                RoomId = "room-1",
                MatchId = "match-1",
                SeatIndex = 3,
                AssignedAtUtc = DateTime.UtcNow
            }),
            cancellationToken);

        var realtimeAttached = await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From(userId),
            (actor, _) => actor.AttachRealtimeAsync(new PlayerRealtimeAttachRequest
            {
                UserId = userId,
                SessionToken = login.SessionToken,
                RoomId = "room-1",
                MatchId = "match-1",
                RealtimeSessionId = "realtime-session-1"
            }),
            cancellationToken);

        Assert.Equal("control-session-1", realtimeAttached.ControlSessionId);
        Assert.Equal("realtime-session-1", realtimeAttached.RealtimeSessionId);

    }

    [Fact]
    public async Task UserActorRejectsRealtimeAttachWhenTokenOrAssignmentDoesNotMatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateActorServices();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var userId = "player-realtime-reject";

        await provider.GetRequiredService<ActorHosting>().EnsureAsync<UserActor>(ActorId.From(userId), cancellationToken);
        var login = await LoginAndAttachUserAsync(
            actors,
            userId,
            "control-connection-1",
            "control-session-1",
            cancellationToken);

        await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From(userId),
            (actor, _) => actor.AssignRoomAsync(new PlayerRoomAssignment
            {
                UserId = userId,
                SessionToken = login.SessionToken,
                RoomId = "room-1",
                MatchId = "match-1",
                SeatIndex = 0,
                AssignedAtUtc = DateTime.UtcNow
            }),
            cancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
                ActorId.From(userId),
                (actor, _) => actor.AttachRealtimeAsync(new PlayerRealtimeAttachRequest
                {
                    UserId = userId,
                    SessionToken = "wrong-token",
                    RoomId = "room-1",
                    MatchId = "match-1",
                    RealtimeSessionId = "realtime-session-1"
                }),
                cancellationToken));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
                ActorId.From(userId),
                (actor, _) => actor.AttachRealtimeAsync(new PlayerRealtimeAttachRequest
                {
                    UserId = userId,
                    SessionToken = login.SessionToken,
                    RoomId = "wrong-room",
                    MatchId = "match-1",
                    RealtimeSessionId = "realtime-session-2"
                }),
                cancellationToken));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
                ActorId.From(userId),
                (actor, _) => actor.AttachRealtimeAsync(new PlayerRealtimeAttachRequest
                {
                    UserId = userId,
                    SessionToken = login.SessionToken,
                    RoomId = "room-1",
                    MatchId = "wrong-match",
                    RealtimeSessionId = "realtime-session-3"
                }),
                cancellationToken));
    }

    [Fact]
    public async Task RoomActorPersistsRealtimeSessionMetadataWhenPlayerIsReady()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateActorServices();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var roomId = "room-realtime-metadata";

        await provider.GetRequiredService<ActorHosting>().EnsureAsync<RoomActor>(ActorId.From(roomId), cancellationToken);
        await actors.AskAsync<RoomActor, RoomSettlementResult>(
            ActorId.From(roomId),
            (actor, _) => actor.CreateAsync(new RoomCreateRequest
            {
                RoomId = roomId,
                MatchId = "match-1",
                CreatedByUserId = "player-1",
                CreatedAtUtc = DateTime.UtcNow,
                Players =
                [
                    new PlayerRoomAssignment
                    {
                        UserId = "player-1",
                        SessionToken = "token-1",
                        ConnectionId = "control-connection-1",
                        RoomId = roomId,
                        MatchId = "match-1",
                        SeatIndex = 0,
                        AssignedAtUtc = DateTime.UtcNow
                    }
                ]
            }),
            cancellationToken);

        var readyAtUtc = DateTime.UtcNow;
        var ready = await actors.AskAsync<RoomActor, RoomSettlementResult>(
            ActorId.From(roomId),
            (actor, _) => actor.SetReadyAsync(new RoomPlayerReadyRequest
            {
                UserId = "player-1",
                RoomId = roomId,
                IsReady = true,
                RealtimeSessionId = "realtime-session-1",
                UpdatedAtUtc = readyAtUtc
            }),
            cancellationToken);

        var player = Assert.Single(ready.Snapshot.Players);
        Assert.True(player.IsReady);
        Assert.True(player.IsConnected);
        Assert.Equal("realtime-session-1", player.RealtimeSessionId);
        Assert.Equal(readyAtUtc, player.LastSeenAtUtc);
    }

    [Fact]
    public async Task UserActorClearsRealtimeSessionOnlyWhenExactSessionMatches()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateActorServices();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var userId = "player-realtime-clear";

        await provider.GetRequiredService<ActorHosting>().EnsureAsync<UserActor>(ActorId.From(userId), cancellationToken);
        var login = await LoginAndAttachUserAsync(
            actors,
            userId,
            "control-connection-1",
            "control-session-1",
            cancellationToken);
        await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From(userId),
            (actor, _) => actor.AssignRoomAsync(new PlayerRoomAssignment
            {
                UserId = userId,
                SessionToken = login.SessionToken,
                RoomId = "room-1",
                MatchId = "match-1",
                SeatIndex = 0,
                AssignedAtUtc = DateTime.UtcNow
            }),
            cancellationToken);
        await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From(userId),
            (actor, _) => actor.AttachRealtimeAsync(new PlayerRealtimeAttachRequest
            {
                UserId = userId,
                SessionToken = login.SessionToken,
                RoomId = "room-1",
                MatchId = "match-1",
                RealtimeSessionId = "realtime-session-2"
            }),
            cancellationToken);

        var staleClear = await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From(userId),
            (actor, _) => actor.ClearRealtimeAsync(new PlayerRealtimeClearRequest
            {
                UserId = userId,
                RealtimeSessionId = "realtime-session-1",
                ClearedAtUtc = DateTime.UtcNow,
                Reason = "Stale expiry"
            }),
            cancellationToken);
        Assert.Equal("realtime-session-2", staleClear.RealtimeSessionId);

        var matchedClear = await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From(userId),
            (actor, _) => actor.ClearRealtimeAsync(new PlayerRealtimeClearRequest
            {
                UserId = userId,
                RealtimeSessionId = "realtime-session-2",
                ClearedAtUtc = DateTime.UtcNow,
                Reason = "Realtime disconnect"
            }),
            cancellationToken);
        Assert.Equal("", matchedClear.RealtimeSessionId);
        Assert.Equal("room-1", matchedClear.CurrentRoomId);
    }

    [Fact]
    public async Task RoomActorClearsRealtimeSessionOnlyWhenExactSessionMatches()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateActorServices();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var roomId = "room-realtime-clear";

        await provider.GetRequiredService<ActorHosting>().EnsureAsync<RoomActor>(ActorId.From(roomId), cancellationToken);
        await actors.AskAsync<RoomActor, RoomSettlementResult>(
            ActorId.From(roomId),
            (actor, _) => actor.CreateAsync(new RoomCreateRequest
            {
                RoomId = roomId,
                MatchId = "match-1",
                CreatedByUserId = "player-1",
                CreatedAtUtc = DateTime.UtcNow,
                Players =
                [
                    new PlayerRoomAssignment
                    {
                        UserId = "player-1",
                        SessionToken = "token-1",
                        ConnectionId = "control-connection-1",
                        RoomId = roomId,
                        MatchId = "match-1",
                        SeatIndex = 0,
                        AssignedAtUtc = DateTime.UtcNow
                    }
                ]
            }),
            cancellationToken);
        await actors.AskAsync<RoomActor, RoomSettlementResult>(
            ActorId.From(roomId),
            (actor, _) => actor.SetReadyAsync(new RoomPlayerReadyRequest
            {
                UserId = "player-1",
                RoomId = roomId,
                IsReady = true,
                RealtimeSessionId = "realtime-session-2",
                UpdatedAtUtc = DateTime.UtcNow
            }),
            cancellationToken);

        var staleClear = await actors.AskAsync<RoomActor, RoomSettlementResult>(
            ActorId.From(roomId),
            (actor, _) => actor.ClearRealtimeAsync(new RoomRealtimeClearRequest
            {
                UserId = "player-1",
                RoomId = roomId,
                RealtimeSessionId = "realtime-session-1",
                ClearedAtUtc = DateTime.UtcNow,
                Reason = "Stale expiry"
            }),
            cancellationToken);
        var stalePlayer = Assert.Single(staleClear.Snapshot.Players);
        Assert.True(stalePlayer.IsReady);
        Assert.True(stalePlayer.IsConnected);
        Assert.Equal("realtime-session-2", stalePlayer.RealtimeSessionId);

        var matchedClear = await actors.AskAsync<RoomActor, RoomSettlementResult>(
            ActorId.From(roomId),
            (actor, _) => actor.ClearRealtimeAsync(new RoomRealtimeClearRequest
            {
                UserId = "player-1",
                RoomId = roomId,
                RealtimeSessionId = "realtime-session-2",
                ClearedAtUtc = DateTime.UtcNow,
                Reason = "Realtime disconnect"
            }),
            cancellationToken);
        var matchedPlayer = Assert.Single(matchedClear.Snapshot.Players);
        Assert.False(matchedPlayer.IsReady);
        Assert.False(matchedPlayer.IsConnected);
        Assert.Equal("", matchedPlayer.RealtimeSessionId);
    }

    [Fact]
    public async Task RoomActorAcceptsInputWhenRealtimeIdentityMatches()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateActorServices();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var roomId = "room-realtime-input-accept";

        await TestHotfix.LoadCurrentRuntimeAsync(provider, cancellationToken);
        await CreateReadyStartedRoomAsync(provider, roomId, cancellationToken);

        var input = await SubmitInputAndReadAsync(
            actors,
            roomId,
            RealtimeInputSessionId,
            moveX: 0.75f,
            moveY: -0.25f,
            lastReceivedServerTick: 0,
            cancellationToken);

        Assert.Equal(0.75f, input.InputX);
        Assert.Equal(-0.25f, input.InputY);
        Assert.Equal(0, input.LastReceivedServerTick);
    }

    [Fact]
    public async Task RoomActorDoesNotDiscardInputWhenReceiveCursorMovesBackward()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateActorServices();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var roomId = "room-receive-cursor-is-not-input-authority";

        await TestHotfix.LoadCurrentRuntimeAsync(provider, cancellationToken);
        await CreateReadyStartedRoomAsync(provider, roomId, cancellationToken);
        await actors.AskAsync<RoomActor, bool>(
            ActorId.From(roomId),
            (actor, _) =>
            {
                GetRoomState(actor).LastPublishedFrame = 100;
                return new ValueTask<bool>(true);
            },
            cancellationToken);

        await SubmitInputAndReadAsync(
            actors,
            roomId,
            RealtimeInputSessionId,
            moveX: 0.75f,
            moveY: 0.25f,
            lastReceivedServerTick: 100,
            cancellationToken);
        var frame = await actors.AskAsync<RoomActor, FrameSyncFrame>(
            ActorId.From(roomId),
            async (actor, _) =>
            {
                await actor.SubmitInputAsync(new RoomInputSubmitRequest
                {
                    RoomId = roomId,
                    UserId = "player-1",
                    RealtimeSessionId = RealtimeInputSessionId,
                    Input = new InputMessage
                    {
                        MoveX = -0.5f,
                        MoveY = -0.25f,
                        LastReceivedServerTick = 95,
                        AddCheatMass = true
                    },
                    SubmittedAtUtc = DateTime.UtcNow
                });
                await actor.RunFrameAsync(new RoomFrameRequest
                {
                    ObservedAtUtc = DateTime.UtcNow
                });
                return GetRoomState(actor).FrameHistory.Last();
            },
            cancellationToken);
        var input = Assert.Single(frame.Inputs, input => input.PlayerId == "player-1");
        Assert.Equal(-0.5f, input.MoveX);
        Assert.Equal(-0.25f, input.MoveY);
        Assert.True(input.AddCheatMass);
        var submitted = await ReadSubmittedInputAsync(actors, roomId, cancellationToken);
        Assert.Equal(100, submitted.LastReceivedServerTick);
    }

    [Fact]
    public async Task RoomActorRelaysStoredInputInNextFrameWithoutServerSimulation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateActorServices();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var roomId = "room-frame-relay";

        await TestHotfix.LoadCurrentRuntimeAsync(provider, cancellationToken);
        await CreateReadyStartedRoomAsync(provider, roomId, cancellationToken);

        await SubmitInputAndReadAsync(
            actors,
            roomId,
            RealtimeInputSessionId,
            moveX: 0.75f,
            moveY: -0.25f,
            lastReceivedServerTick: 0,
            cancellationToken);
        var frame = await actors.AskAsync<RoomActor, FrameSyncFrame>(
            ActorId.From(roomId),
            async (actor, _) =>
            {
                await actor.RunFrameAsync(new RoomFrameRequest
                {
                    ObservedAtUtc = DateTime.UtcNow
                });
                return GetRoomState(actor).FrameHistory.Last();
            },
            cancellationToken);

        var input = Assert.Single(frame.Inputs, input => input.PlayerId == "player-1");
        Assert.Equal(0.75f, input.MoveX);
        Assert.Equal(-0.25f, input.MoveY);
        Assert.Equal(frame.Frame, input.ServerTick);
        Assert.Null(typeof(RoomActor).GetField("RuntimeSimulation", BindingFlags.Instance | BindingFlags.NonPublic));
    }

    [Theory]
    [InlineData("stale-realtime-session")]
    [InlineData("")]
    public async Task RoomActorRejectsInputWhenRealtimeIdentityDoesNotMatch(string requestRealtimeSessionId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateActorServices();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var roomId = $"room-realtime-input-reject-{DescribeRealtimeIdentity(requestRealtimeSessionId)}";

        await TestHotfix.LoadCurrentRuntimeAsync(provider, cancellationToken);
        await CreateReadyStartedRoomAsync(provider, roomId, cancellationToken);
        var before = await ReadSubmittedInputAsync(actors, roomId, cancellationToken);

        var after = await SubmitInputAndReadAsync(
            actors,
            roomId,
            requestRealtimeSessionId,
            moveX: 1f,
            moveY: 0.5f,
            lastReceivedServerTick: 456,
            cancellationToken);

        Assert.Equal(before, after);
    }

    private static async Task CreateReadyStartedRoomAsync(
        IServiceProvider services,
        string roomId,
        CancellationToken cancellationToken)
    {
        var actors = services.GetRequiredService<IActorRuntime>();
        var hosting = services.GetRequiredService<ActorHosting>();
        await hosting.EnsureAsync<RoomActor>(ActorId.From(roomId), cancellationToken);
        await actors.AskAsync<RoomActor, RoomSettlementResult>(
            ActorId.From(roomId),
            (actor, _) => actor.CreateAsync(new RoomCreateRequest
            {
                RoomId = roomId,
                MatchId = "match-1",
                CreatedByUserId = "player-1",
                CreatedAtUtc = DateTime.UtcNow,
                Players =
                [
                    new PlayerRoomAssignment
                    {
                        UserId = "player-1",
                        SessionToken = "token-1",
                        ConnectionId = "control-connection-1",
                        RoomId = roomId,
                        MatchId = "match-1",
                        SeatIndex = 0,
                        AssignedAtUtc = DateTime.UtcNow
                    }
                ]
            }),
            cancellationToken);
        await actors.AskAsync<RoomActor, RoomSettlementResult>(
            ActorId.From(roomId),
            (actor, _) => actor.SetReadyAsync(new RoomPlayerReadyRequest
            {
                UserId = "player-1",
                RoomId = roomId,
                IsReady = true,
                RealtimeSessionId = RealtimeInputSessionId,
                UpdatedAtUtc = DateTime.UtcNow
            }),
            cancellationToken);
        await services.GetRequiredService<ActorAccess>()
            .Local<RoomActor>(new RoomId(roomId))
            .CallAsync(
                static behavior => behavior.StartAsync,
                new RoomStartRequest
                {
                    RoomId = roomId,
                    StartedByUserId = "player-1",
                    StartedAtUtc = DateTime.UtcNow
                },
                cancellationToken);
    }

    private static ValueTask<SubmittedInputState> SubmitInputAndReadAsync(
        IActorRuntime actors,
        string roomId,
        string realtimeSessionId,
        float moveX,
        float moveY,
        int lastReceivedServerTick,
        CancellationToken cancellationToken)
    {
        return actors.AskAsync<RoomActor, SubmittedInputState>(
            ActorId.From(roomId),
            async (actor, _) =>
            {
                await actor.SubmitInputAsync(new RoomInputSubmitRequest
                {
                    RoomId = roomId,
                    UserId = "player-1",
                    RealtimeSessionId = realtimeSessionId,
                    Input = new InputMessage
                    {
                        MoveX = moveX,
                        MoveY = moveY,
                        LastReceivedServerTick = lastReceivedServerTick
                    },
                    SubmittedAtUtc = DateTime.UtcNow
                });

                return ReadSubmittedInput(actor);
            },
            cancellationToken);
    }

    private static ValueTask<SubmittedInputState> ReadSubmittedInputAsync(
        IActorRuntime actors,
        string roomId,
        CancellationToken cancellationToken)
    {
        return actors.AskAsync<RoomActor, SubmittedInputState>(
            ActorId.From(roomId),
            (actor, _) => new ValueTask<SubmittedInputState>(ReadSubmittedInput(actor)),
            cancellationToken);
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

    private static ServiceProvider CreateActorServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        services.AddGeneratedActorSelectorTestDependencies();
        return services.BuildReadyServiceProvider();
    }

    private static string DescribeRealtimeIdentity(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "blank"
            : value.Replace(" ", "-", StringComparison.Ordinal);
    }

    private static SubmittedInputState ReadSubmittedInput(RoomActor actor)
    {
        var state = GetRoomState(actor);
        var player = state.Players.Single(player => string.Equals(player.UserId, "player-1", StringComparison.Ordinal));
        return new SubmittedInputState(player.InputX, player.InputY, player.LastReceivedServerTick);
    }

    private static RoomState GetRoomState(RoomActor actor)
    {
        var stateField = typeof(RoomActor).GetField("State", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (RoomState)stateField.GetValue(actor)!;
    }

    private sealed record SubmittedInputState(float InputX, float InputY, int LastReceivedServerTick);
}
