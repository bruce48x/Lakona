using System.Reflection;
using Server.App.State.Contracts.Rooms;
using Server.App.State.Contracts.Sessions;
using Server.App.State.Contracts.Users;
using Server.App.State.Rooms;
using Server.App.State.Users;
using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using Microsoft.Extensions.DependencyInjection;
using Server.Hotfix.State.Rooms;
using Server.Hotfix.State.Users;
using Shared.Interfaces;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class PlayerSessionActorStateTests
{
    private const string RealtimeInputSessionId = "realtime-session-1";
    private const long RealtimeInputSessionGeneration = 3;

    [Fact]
    public async Task UserActorPersistsControlAndRealtimeFrameworkSessionMetadata()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateActorServices();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var userId = "player-session-metadata";
        var attachedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        var realtimeAttachedAtUtc = DateTime.UtcNow;

        await provider.GetRequiredService<ActorHosting>().EnsureAsync<UserActor>(ActorId.From(userId), cancellationToken);

        var attached = await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From(userId),
            (actor, _) => actor.AttachAsync(new PlayerSessionAttachRequest
            {
                UserId = userId,
                SessionToken = "token-1",
                ConnectionId = "control-connection-1",
                ControlSessionId = "control-session-1",
                ControlSessionGeneration = 7,
                AttachedAtUtc = attachedAtUtc
            }),
            cancellationToken);

        Assert.Equal("control-session-1", attached.ControlSessionId);
        Assert.Equal(7, attached.ControlSessionGeneration);
        Assert.Equal("", attached.RealtimeSessionId);
        Assert.Equal(0, attached.RealtimeSessionGeneration);

        await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From(userId),
            (actor, _) => actor.AssignRoomAsync(new PlayerRoomAssignment
            {
                UserId = userId,
                SessionToken = "token-1",
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
                SessionToken = "token-1",
                RoomId = "room-1",
                MatchId = "match-1",
                RealtimeSessionId = "realtime-session-1",
                RealtimeSessionGeneration = 11,
                AttachedAtUtc = realtimeAttachedAtUtc
            }),
            cancellationToken);

        Assert.Equal("control-session-1", realtimeAttached.ControlSessionId);
        Assert.Equal(7, realtimeAttached.ControlSessionGeneration);
        Assert.Equal("realtime-session-1", realtimeAttached.RealtimeSessionId);
        Assert.Equal(11, realtimeAttached.RealtimeSessionGeneration);
        Assert.Equal(realtimeAttachedAtUtc, realtimeAttached.LastConnectedAtUtc);
        Assert.Equal(realtimeAttachedAtUtc, realtimeAttached.LastHeartbeatAtUtc);

    }

    [Fact]
    public async Task UserActorRejectsRealtimeAttachWhenTokenOrAssignmentDoesNotMatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateActorServices();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var userId = "player-realtime-reject";

        await provider.GetRequiredService<ActorHosting>().EnsureAsync<UserActor>(ActorId.From(userId), cancellationToken);
        await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From(userId),
            (actor, _) => actor.AttachAsync(new PlayerSessionAttachRequest
            {
                UserId = userId,
                SessionToken = "token-1",
                ControlSessionId = "control-session-1",
                ControlSessionGeneration = 1,
                AttachedAtUtc = DateTime.UtcNow
            }),
            cancellationToken);

        await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From(userId),
            (actor, _) => actor.AssignRoomAsync(new PlayerRoomAssignment
            {
                UserId = userId,
                SessionToken = "token-1",
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
                    RealtimeSessionId = "realtime-session-1",
                    RealtimeSessionGeneration = 2,
                    AttachedAtUtc = DateTime.UtcNow
                }),
                cancellationToken));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
                ActorId.From(userId),
                (actor, _) => actor.AttachRealtimeAsync(new PlayerRealtimeAttachRequest
                {
                    UserId = userId,
                    SessionToken = "token-1",
                    RoomId = "wrong-room",
                    MatchId = "match-1",
                    RealtimeSessionId = "realtime-session-2",
                    RealtimeSessionGeneration = 3,
                    AttachedAtUtc = DateTime.UtcNow
                }),
                cancellationToken));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
                ActorId.From(userId),
                (actor, _) => actor.AttachRealtimeAsync(new PlayerRealtimeAttachRequest
                {
                    UserId = userId,
                    SessionToken = "token-1",
                    RoomId = "room-1",
                    MatchId = "wrong-match",
                    RealtimeSessionId = "realtime-session-3",
                    RealtimeSessionGeneration = 4,
                    AttachedAtUtc = DateTime.UtcNow
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
                RealtimeSessionGeneration = 3,
                UpdatedAtUtc = readyAtUtc
            }),
            cancellationToken);

        var player = Assert.Single(ready.Snapshot.Players);
        Assert.True(player.IsReady);
        Assert.True(player.IsConnected);
        Assert.Equal("realtime-session-1", player.RealtimeSessionId);
        Assert.Equal(3, player.RealtimeSessionGeneration);
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
        await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From(userId),
            (actor, _) => actor.AttachAsync(new PlayerSessionAttachRequest
            {
                UserId = userId,
                SessionToken = "token-1",
                ControlSessionId = "control-session-1",
                ControlSessionGeneration = 1,
                AttachedAtUtc = DateTime.UtcNow
            }),
            cancellationToken);
        await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From(userId),
            (actor, _) => actor.AssignRoomAsync(new PlayerRoomAssignment
            {
                UserId = userId,
                SessionToken = "token-1",
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
                SessionToken = "token-1",
                RoomId = "room-1",
                MatchId = "match-1",
                RealtimeSessionId = "realtime-session-2",
                RealtimeSessionGeneration = 2,
                AttachedAtUtc = DateTime.UtcNow
            }),
            cancellationToken);

        var staleClear = await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From(userId),
            (actor, _) => actor.ClearRealtimeAsync(new PlayerRealtimeClearRequest
            {
                UserId = userId,
                RealtimeSessionId = "realtime-session-1",
                RealtimeSessionGeneration = 1,
                ClearedAtUtc = DateTime.UtcNow,
                Reason = "Stale expiry"
            }),
            cancellationToken);
        Assert.Equal("realtime-session-2", staleClear.RealtimeSessionId);
        Assert.Equal(2, staleClear.RealtimeSessionGeneration);

        var matchedClear = await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From(userId),
            (actor, _) => actor.ClearRealtimeAsync(new PlayerRealtimeClearRequest
            {
                UserId = userId,
                RealtimeSessionId = "realtime-session-2",
                RealtimeSessionGeneration = 2,
                ClearedAtUtc = DateTime.UtcNow,
                Reason = "Realtime disconnect"
            }),
            cancellationToken);
        Assert.Equal("", matchedClear.RealtimeSessionId);
        Assert.Equal(0, matchedClear.RealtimeSessionGeneration);
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
                RealtimeSessionGeneration = 2,
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
                RealtimeSessionGeneration = 1,
                ClearedAtUtc = DateTime.UtcNow,
                Reason = "Stale expiry"
            }),
            cancellationToken);
        var stalePlayer = Assert.Single(staleClear.Snapshot.Players);
        Assert.True(stalePlayer.IsReady);
        Assert.True(stalePlayer.IsConnected);
        Assert.Equal("realtime-session-2", stalePlayer.RealtimeSessionId);
        Assert.Equal(2, stalePlayer.RealtimeSessionGeneration);

        var matchedClear = await actors.AskAsync<RoomActor, RoomSettlementResult>(
            ActorId.From(roomId),
            (actor, _) => actor.ClearRealtimeAsync(new RoomRealtimeClearRequest
            {
                UserId = "player-1",
                RoomId = roomId,
                RealtimeSessionId = "realtime-session-2",
                RealtimeSessionGeneration = 2,
                ClearedAtUtc = DateTime.UtcNow,
                Reason = "Realtime disconnect"
            }),
            cancellationToken);
        var matchedPlayer = Assert.Single(matchedClear.Snapshot.Players);
        Assert.False(matchedPlayer.IsReady);
        Assert.False(matchedPlayer.IsConnected);
        Assert.Equal("", matchedPlayer.RealtimeSessionId);
        Assert.Equal(0, matchedPlayer.RealtimeSessionGeneration);
    }

    [Fact]
    public async Task RoomActorAcceptsInputWhenRealtimeIdentityMatches()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateActorServices();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var roomId = "room-realtime-input-accept";

        await CreateReadyStartedRoomAsync(actors, provider.GetRequiredService<ActorHosting>(), roomId, cancellationToken);

        var input = await SubmitInputAndReadAsync(
            actors,
            roomId,
            RealtimeInputSessionId,
            RealtimeInputSessionGeneration,
            moveX: 0.75f,
            moveY: -0.25f,
            tick: 123,
            cancellationToken);

        Assert.Equal(0.75f, input.InputX);
        Assert.Equal(-0.25f, input.InputY);
        Assert.Equal(123, input.LastInputTick);
    }

    [Theory]
    [InlineData("stale-realtime-session", RealtimeInputSessionGeneration)]
    [InlineData(RealtimeInputSessionId, 2)]
    [InlineData("", RealtimeInputSessionGeneration)]
    [InlineData(RealtimeInputSessionId, 0)]
    public async Task RoomActorRejectsInputWhenRealtimeIdentityDoesNotMatch(
        string requestRealtimeSessionId,
        long requestRealtimeSessionGeneration)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateActorServices();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var roomId = $"room-realtime-input-reject-{DescribeRealtimeIdentity(requestRealtimeSessionId)}-{requestRealtimeSessionGeneration}";

        await CreateReadyStartedRoomAsync(actors, provider.GetRequiredService<ActorHosting>(), roomId, cancellationToken);
        var before = await ReadSubmittedInputAsync(actors, roomId, cancellationToken);

        var after = await SubmitInputAndReadAsync(
            actors,
            roomId,
            requestRealtimeSessionId,
            requestRealtimeSessionGeneration,
            moveX: 1f,
            moveY: 0.5f,
            tick: 456,
            cancellationToken);

        Assert.Equal(before, after);
    }

    private static async Task CreateReadyStartedRoomAsync(
        IActorRuntime actors,
        ActorHosting hosting,
        string roomId,
        CancellationToken cancellationToken)
    {
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
                RealtimeSessionGeneration = RealtimeInputSessionGeneration,
                UpdatedAtUtc = DateTime.UtcNow
            }),
            cancellationToken);
        await actors.AskAsync<RoomActor, RoomSettlementResult>(
            ActorId.From(roomId),
            (actor, _) => actor.StartAsync(new RoomStartRequest
            {
                RoomId = roomId,
                StartedByUserId = "player-1",
                StartedAtUtc = DateTime.UtcNow
            }),
            cancellationToken);
    }

    private static ValueTask<SubmittedInputState> SubmitInputAndReadAsync(
        IActorRuntime actors,
        string roomId,
        string realtimeSessionId,
        long realtimeSessionGeneration,
        float moveX,
        float moveY,
        int tick,
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
                    RealtimeSessionGeneration = realtimeSessionGeneration,
                    Input = new InputMessage
                    {
                        MoveX = moveX,
                        MoveY = moveY,
                        Tick = tick
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

    private static ServiceProvider CreateActorServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        services.AddGeneratedActorSelectorTestDependencies();
        return services.BuildServiceProvider();
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
        var player = state.Simulation.Players.Single(player => string.Equals(player.PlayerId, "player-1", StringComparison.Ordinal));
        return new SubmittedInputState(player.InputX, player.InputY, player.LastInputTick);
    }

    private static RoomState GetRoomState(RoomActor actor)
    {
        var stateField = typeof(RoomActor).GetField("State", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (RoomState)stateField.GetValue(actor)!;
    }

    private sealed record SubmittedInputState(float InputX, float InputY, int LastInputTick);
}
