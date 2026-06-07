using Gateway.Services;
using Shared.Interfaces;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class SessionDirectoryCleanupTests
{
    [Fact]
    public void ClearRoomDetachesRealtimeCallbackWhenExpectedRoomMatches()
    {
        var directory = new SessionDirectory();
        var controlCallback = new TestPlayerCallback();
        var realtimeCallback = new TestPlayerCallback();

        directory.Register("player-1", "session-1", "control-1", controlCallback, preserveSessionState: false);
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
        Assert.Same(controlCallback, registration.GetRealtimePreferredCallback());
        Assert.Empty(directory.GetByRoom("room-1"));
    }

    [Fact]
    public void ClearRoomPreservesRegistrationWhenExpectedRoomDoesNotMatch()
    {
        var directory = new SessionDirectory();
        var realtimeCallback = new TestPlayerCallback();

        directory.Register("player-1", "session-1", "control-1", new TestPlayerCallback(), preserveSessionState: false);
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
            new TestPlayerCallback()));

        directory.ClearRoom("player-1", "room-1");

        Assert.Null(directory.Get("player-1"));
        Assert.Empty(directory.GetByRoom("room-1"));
    }

    [Fact]
    public void RegisterWithoutPreserveSessionStateClearsRoomQueueAndRealtimeState()
    {
        var directory = new SessionDirectory();

        directory.Register("player-1", "session-1", "control-1", new TestPlayerCallback(), preserveSessionState: false);
        directory.SetQueueTicket("player-1", "ticket-1");
        directory.AssignRoom("player-1", "room-1", "match-1", seatIndex: 1);
        Assert.True(directory.AttachRealtime(
            "player-1",
            "session-1",
            "room-1",
            "match-1",
            "realtime-1",
            new TestPlayerCallback()));

        directory.Register("player-1", "session-2", "control-2", new TestPlayerCallback(), preserveSessionState: false);

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

    private sealed class TestPlayerCallback : IPlayerCallback
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
