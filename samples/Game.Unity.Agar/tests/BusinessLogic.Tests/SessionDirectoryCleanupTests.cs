using Lakona.Game.Server.Sessions;
using Server.Hotfix.Services;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class PlayerSessionRegistryCleanupTests
{
    [Fact]
    public void ClearRoomDetachesRealtimeSessionWhenExpectedRoomMatches()
    {
        var directory = new PlayerSessionRegistry();

        directory.RegisterControl("player-1", "session-1", "control-1", new GameSessionKey("player-1", "control-session", 1));
        directory.AssignRoom("player-1", "room-1", "match-1", seatIndex: 3);
        Assert.True(directory.AttachRealtime("player-1", "session-1", "room-1", "match-1", "realtime-1", new GameSessionKey("player-1", "realtime-session", 2)));

        directory.ClearRoom("player-1", "room-1");

        var registration = directory.Get("player-1");
        Assert.NotNull(registration);
        Assert.Null(registration.RoomId);
        Assert.Null(registration.MatchId);
        Assert.Equal(-1, registration.SeatIndex);
        Assert.Null(registration.RealtimeConnectionId);
        Assert.Null(registration.RealtimeSessionKey);
        Assert.Empty(directory.GetByRoom("room-1"));
    }

    [Fact]
    public void ClearRoomPreservesRegistrationWhenExpectedRoomDoesNotMatch()
    {
        var directory = new PlayerSessionRegistry();

        var realtimeSession = new GameSessionKey("player-1", "realtime-session", 2);
        directory.RegisterControl("player-1", "session-1", "control-1", new GameSessionKey("player-1", "control-session", 1));
        directory.AssignRoom("player-1", "room-1", "match-1", seatIndex: 2);
        Assert.True(directory.AttachRealtime("player-1", "session-1", "room-1", "match-1", "realtime-1", realtimeSession));

        directory.ClearRoom("player-1", "other-room");

        var registration = directory.Get("player-1");
        Assert.NotNull(registration);
        Assert.Equal("room-1", registration.RoomId);
        Assert.Equal("match-1", registration.MatchId);
        Assert.Equal(2, registration.SeatIndex);
        Assert.Equal("realtime-1", registration.RealtimeConnectionId);
        Assert.Equal(realtimeSession, registration.RealtimeSessionKey);
        Assert.Single(directory.GetByRoom("room-1"));
    }

    [Fact]
    public void ClearRoomRemovesRealtimeOnlyRegistration()
    {
        var directory = new PlayerSessionRegistry();

        Assert.True(directory.AttachRealtime(
            "player-1",
            "session-1",
            "room-1",
            "match-1",
            "realtime-1",
            new GameSessionKey("player-1", "realtime-session", 1)));

        directory.ClearRoom("player-1", "room-1");

        Assert.Null(directory.Get("player-1"));
        Assert.Empty(directory.GetByRoom("room-1"));
    }

    [Fact]
    public void RegisterControlClearsRoomQueueAndRealtimeState()
    {
        var directory = new PlayerSessionRegistry();

        directory.RegisterControl("player-1", "session-1", "control-1", new GameSessionKey("player-1", "control-session-1", 1));
        directory.SetQueueTicket("player-1", "ticket-1");
        directory.AssignRoom("player-1", "room-1", "match-1", seatIndex: 1);
        Assert.True(directory.AttachRealtime(
            "player-1",
            "session-1",
            "room-1",
            "match-1",
            "realtime-1",
            new GameSessionKey("player-1", "realtime-session", 2)));

        directory.RegisterControl("player-1", "session-2", "control-2", new GameSessionKey("player-1", "control-session-2", 3));

        var registration = directory.Get("player-1");
        Assert.NotNull(registration);
        Assert.Equal("session-2", registration.SessionToken);
        Assert.Equal("control-2", registration.ConnectionId);
        Assert.Null(registration.RoomId);
        Assert.Null(registration.MatchId);
        Assert.Equal(-1, registration.SeatIndex);
        Assert.Null(registration.MatchmakingTicketId);
        Assert.Null(registration.RealtimeConnectionId);
        Assert.Null(registration.RealtimeSessionKey);
        Assert.Empty(directory.GetByRoom("room-1"));
    }
}
