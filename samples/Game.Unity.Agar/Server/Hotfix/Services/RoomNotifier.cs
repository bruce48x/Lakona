using Microsoft.Extensions.Logging;
using Shared.Interfaces;
using Lakona.Game.Server;

namespace Server.Hotfix.Services;

internal sealed class RoomNotifier
{
    private readonly ILakonaGameServer _gameServer;
    private readonly PlayerSessionRegistry _sessions;
    private readonly ILogger<RoomNotifier> _logger;

    public RoomNotifier(ILakonaGameServer gameServer, PlayerSessionRegistry sessions, ILogger<RoomNotifier> logger)
    {
        _gameServer = gameServer;
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
            if (registration.RealtimeSessionKey is not { } realtimeSession)
            {
                continue;
            }

            var callback = _gameServer.GetCallbackAsync<IBattleCallback>(realtimeSession)
                .AsTask()
                .GetAwaiter()
                .GetResult();
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
