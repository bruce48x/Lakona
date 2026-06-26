using Agar.Sample.State.Contracts.Rooms;
using Microsoft.Extensions.Logging;
using Shared.Interfaces;
using Lakona.Game.Server.Sessions;

namespace Server.Hotfix.Services;

internal sealed class RoomNotifier
{
    private readonly IClientNotificationRelay _notifications;
    private readonly ILogger<RoomNotifier> _logger;

    public RoomNotifier(IClientNotificationRelay notifications, ILogger<RoomNotifier> logger)
    {
        _notifications = notifications;
        _logger = logger;
    }

    public ValueTask PublishWorldStateAsync(RoomSnapshot room, WorldState worldState, CancellationToken cancellationToken = default)
    {
        return PublishAsync(room, callback => callback.OnWorldState(worldState), cancellationToken);
    }

    public ValueTask PublishPlayerDeadAsync(RoomSnapshot room, PlayerDead playerDead, CancellationToken cancellationToken = default)
    {
        return PublishAsync(room, callback => callback.OnPlayerDead(playerDead), cancellationToken);
    }

    public ValueTask PublishMatchEndAsync(RoomSnapshot room, MatchEnd matchEnd, CancellationToken cancellationToken = default)
    {
        return PublishAsync(room, callback => callback.OnMatchEnd(matchEnd), cancellationToken);
    }

    private async ValueTask PublishAsync(
        RoomSnapshot room,
        Action<IBattleCallback> notify,
        CancellationToken cancellationToken)
    {
        foreach (var player in room.Players)
        {
            if (string.IsNullOrWhiteSpace(player.RealtimeSessionId) ||
                player.RealtimeSessionGeneration <= 0)
            {
                continue;
            }

            var realtimeSession = new GameSessionKey(
                player.UserId,
                player.RealtimeSessionId,
                player.RealtimeSessionGeneration);
            var status = await _notifications
                .NotifyAsync(realtimeSession, notify, cancellationToken)
                .ConfigureAwait(false);
            if (status == ClientNotificationStatus.Delivered)
            {
                continue;
            }

            _logger.LogDebug(
                "Room notification delivery returned {Status} for room {RoomId} session {Session}.",
                status,
                room.RoomId,
                realtimeSession);
        }
    }
}
