using Server.App.Services;
using Shared.Interfaces;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class SessionDirectoryCleanupTests
{
    [Fact]
    public async Task ClearRoomDetachesRealtimeCallbackWhenExpectedRoomMatches()
    {
        var directory = new SessionDirectory();
        var controlCallback = new TestControlCallback();
        var realtimeCallback = new TestBattleCallback();

        await directory.RegisterNewControlAsync("player-1", "session-1", "control-1", TestContext.Current.CancellationToken);
        Assert.True(await directory.BindControlCallbackAsync("player-1", "control-1", controlCallback, TestContext.Current.CancellationToken));
        directory.AssignRoom("player-1", "room-1", "match-1", seatIndex: 3);
        Assert.True(directory.AttachRealtime("player-1", "session-1", "room-1", "match-1", "realtime-1", realtimeCallback));

        directory.ClearRoom("player-1", "room-1");

        var registration = directory.Get("player-1");
        Assert.NotNull(registration);
        Assert.Null(registration.RoomId);
        Assert.Null(registration.MatchId);
        Assert.Equal(-1, registration.SeatIndex);
        Assert.Null(registration.RealtimeConnectionId);
        Assert.Null(registration.RealtimeCallback);
        Assert.Null(registration.GetRealtimeCallback());
        Assert.Empty(directory.GetByRoom("room-1"));
    }

    [Fact]
    public async Task ClearRoomPreservesRegistrationWhenExpectedRoomDoesNotMatch()
    {
        var directory = new SessionDirectory();
        var controlCallback = new TestControlCallback();
        var realtimeCallback = new TestBattleCallback();

        await directory.RegisterNewControlAsync("player-1", "session-1", "control-1", TestContext.Current.CancellationToken);
        Assert.True(await directory.BindControlCallbackAsync("player-1", "control-1", controlCallback, TestContext.Current.CancellationToken));
        directory.AssignRoom("player-1", "room-1", "match-1", seatIndex: 2);
        Assert.True(directory.AttachRealtime("player-1", "session-1", "room-1", "match-1", "realtime-1", realtimeCallback));

        directory.ClearRoom("player-1", "other-room");

        var registration = directory.Get("player-1");
        Assert.NotNull(registration);
        Assert.Equal("room-1", registration.RoomId);
        Assert.Equal("match-1", registration.MatchId);
        Assert.Equal(2, registration.SeatIndex);
        Assert.Equal("realtime-1", registration.RealtimeConnectionId);
        Assert.Same(realtimeCallback, registration.RealtimeCallback);
        Assert.Single(directory.GetByRoom("room-1"));
    }

    [Fact]
    public void ClearRoomRemovesRealtimeOnlyRegistration()
    {
        var directory = new SessionDirectory();

        Assert.True(directory.AttachRealtime(
            "player-1",
            "session-1",
            "room-1",
            "match-1",
            "realtime-1",
            new TestBattleCallback()));

        directory.ClearRoom("player-1", "room-1");

        Assert.Null(directory.Get("player-1"));
        Assert.Empty(directory.GetByRoom("room-1"));
    }

    [Fact]
    public async Task RegisterNewControlClearsRoomQueueAndRealtimeState()
    {
        var directory = new SessionDirectory();

        await directory.RegisterNewControlAsync("player-1", "session-1", "control-1", TestContext.Current.CancellationToken);
        Assert.True(await directory.BindControlCallbackAsync("player-1", "control-1", new TestControlCallback(), TestContext.Current.CancellationToken));
        directory.SetQueueTicket("player-1", "ticket-1");
        directory.AssignRoom("player-1", "room-1", "match-1", seatIndex: 1);
        Assert.True(directory.AttachRealtime(
            "player-1",
            "session-1",
            "room-1",
            "match-1",
            "realtime-1",
            new TestBattleCallback()));

        await directory.RegisterNewControlAsync("player-1", "session-2", "control-2", TestContext.Current.CancellationToken);
        Assert.True(await directory.BindControlCallbackAsync("player-1", "control-2", new TestControlCallback(), TestContext.Current.CancellationToken));

        var registration = directory.Get("player-1");
        Assert.NotNull(registration);
        Assert.Equal("session-2", registration.SessionToken);
        Assert.Equal("control-2", registration.ConnectionId);
        Assert.Null(registration.RoomId);
        Assert.Null(registration.MatchId);
        Assert.Equal(-1, registration.SeatIndex);
        Assert.Null(registration.MatchmakingTicketId);
        Assert.Null(registration.RealtimeConnectionId);
        Assert.Null(registration.RealtimeCallback);
        Assert.Empty(directory.GetByRoom("room-1"));
    }

    private sealed class TestControlCallback : IControlCallback
    {
        public MatchmakingStatusUpdate? LastStatus { get; private set; }

        public void OnMatchmakingStatus(MatchmakingStatusUpdate matchmakingStatus)
        {
            LastStatus = matchmakingStatus;
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
