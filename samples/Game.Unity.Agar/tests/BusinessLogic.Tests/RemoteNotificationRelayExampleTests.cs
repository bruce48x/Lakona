using Lakona.Game.Server.Sessions;
using Shared.Interfaces;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class RemoteNotificationRelayExampleTests
{
    [Fact]
    public async Task RemoteMatchmakingNotificationCanRelayToGatewayCallback()
    {
        var sessions = new InMemoryGameSessionDirectory();
        var session = await sessions.StartNewSessionAsync("player-1", TestContext.Current.CancellationToken);
        var callback = new CapturingPlayerCallback();
        await sessions.BindSessionAsync(session, "control-1", callback, TestContext.Current.CancellationToken);
        var relay = new ClientNotificationRelay(sessions);

        var update = new MatchmakingStatusUpdate
        {
            State = MatchmakingState.Matched,
            RoomId = "room-1",
            MatchedPlayerCount = 2,
            Message = "Matched into room room-1"
        };

        var status = await relay.NotifyAsync<IPlayerCallback>(
            session,
            target => target.OnMatchmakingStatus(update),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Delivered, status);
        Assert.Same(update, callback.LastMatchmakingStatus);
        Assert.Equal(MatchmakingState.Matched, callback.LastMatchmakingStatus?.State);
        Assert.Equal("room-1", callback.LastMatchmakingStatus?.RoomId);
    }

    private sealed class CapturingPlayerCallback : IPlayerCallback
    {
        public MatchmakingStatusUpdate? LastMatchmakingStatus { get; private set; }

        public void OnWorldState(WorldState worldState)
        {
        }

        public void OnPlayerDead(PlayerDead deadEvent)
        {
        }

        public void OnMatchEnd(MatchEnd matchEnd)
        {
        }

        public void OnMatchmakingStatus(MatchmakingStatusUpdate matchmakingStatus)
        {
            LastMatchmakingStatus = matchmakingStatus;
        }
    }
}
