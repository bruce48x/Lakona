using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Contracts.Users;
using Agar.Sample.State.Rooms;
using Agar.Sample.State.Users;
using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using Microsoft.Extensions.DependencyInjection;
using Server.Hotfix.State.Rooms;
using Server.Hotfix.State.Sessions;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class PlayerSessionActorStateTests
{
    [Fact]
    public async Task UserActorPersistsControlAndRealtimeFrameworkSessionMetadata()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actors = CreateActorRuntime();
        var userId = "player-session-metadata";
        var attachedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        var realtimeAttachedAtUtc = DateTime.UtcNow;

        await ((IActorLifecycle)actors).CreateLocalAsync<UserActor>(ActorId.From(userId), cancellationToken: cancellationToken);

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

        var reconnected = await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From(userId),
            (actor, _) => actor.ReconnectAsync(new PlayerSessionReconnectRequest
            {
                UserId = userId,
                SessionToken = "token-1",
                ConnectionId = "control-connection-2",
                ControlSessionId = "control-session-2",
                ControlSessionGeneration = 12,
                ReconnectedAtUtc = DateTime.UtcNow
            }),
            cancellationToken);

        Assert.Equal("control-session-2", reconnected.ControlSessionId);
        Assert.Equal(12, reconnected.ControlSessionGeneration);
        Assert.Equal("realtime-session-1", reconnected.RealtimeSessionId);
        Assert.Equal(11, reconnected.RealtimeSessionGeneration);
    }

    [Fact]
    public async Task UserActorRejectsRealtimeAttachWhenTokenOrAssignmentDoesNotMatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actors = CreateActorRuntime();
        var userId = "player-realtime-reject";

        await ((IActorLifecycle)actors).CreateLocalAsync<UserActor>(ActorId.From(userId), cancellationToken: cancellationToken);
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
        var actors = CreateActorRuntime();
        var roomId = "room-realtime-metadata";

        await ((IActorLifecycle)actors).CreateLocalAsync<RoomActor>(ActorId.From(roomId), cancellationToken: cancellationToken);
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
        var actors = CreateActorRuntime();
        var userId = "player-realtime-clear";

        await ((IActorLifecycle)actors).CreateLocalAsync<UserActor>(ActorId.From(userId), cancellationToken: cancellationToken);
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
        var actors = CreateActorRuntime();
        var roomId = "room-realtime-clear";

        await ((IActorLifecycle)actors).CreateLocalAsync<RoomActor>(ActorId.From(roomId), cancellationToken: cancellationToken);
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

    private static IActorRuntime CreateActorRuntime()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        services.AddGeneratedActorSelectorTestDependencies();
        return services.BuildServiceProvider().GetRequiredService<IActorRuntime>();
    }
}
