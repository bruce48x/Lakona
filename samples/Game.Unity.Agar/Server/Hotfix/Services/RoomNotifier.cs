using Microsoft.Extensions.Logging;
using Shared.Interfaces;

namespace Server.Hotfix.Services;

internal sealed class RoomNotifier
{
    private readonly PlayerSessionRegistry _sessions;
    private readonly ILogger<RoomNotifier> _logger;

    public RoomNotifier(PlayerSessionRegistry sessions, ILogger<RoomNotifier> logger)
    {
        _sessions = sessions;
        _logger = logger;
    }

    public void PublishWorldState(string roomId, WorldState worldState)
    {
        Publish(roomId, callback => callback.OnWorldState(worldState));
    }

    public void PublishPlayerDead(string roomId, PlayerDead playerDead)
    {
        Publish(roomId, callback => callback.OnPlayerDead(playerDead));
    }

    public void PublishMatchEnd(string roomId, MatchEnd matchEnd)
    {
        Publish(roomId, callback => callback.OnMatchEnd(matchEnd));
    }

    private void Publish(string roomId, Action<IBattleCallback> action)
    {
        foreach (var registration in _sessions.GetByRoom(roomId))
        {
            var callback = registration.GetRealtimeCallback();
            if (callback is null)
            {
                continue;
            }

            try
            {
                action(callback);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish room callback for room {RoomId}.", roomId);
            }
        }
    }
}
