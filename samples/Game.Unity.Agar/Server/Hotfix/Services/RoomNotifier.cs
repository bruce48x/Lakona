using Server.App.State.Contracts.Rooms;
using Microsoft.Extensions.Logging;
using Shared.Interfaces;
using Lakona.Game.Server.Sessions;

namespace Server.Hotfix.Services;

internal sealed class RoomNotifier
{
    private readonly IClientNotifications _notifications;
    private readonly ILogger<RoomNotifier> _logger;

    public RoomNotifier(IClientNotifications notifications, ILogger<RoomNotifier> logger)
    {
        _notifications = notifications;
        _logger = logger;
    }

    public ValueTask PublishWorldStateAsync(RoomSnapshot room, WorldState worldState, CancellationToken cancellationToken = default)
    {
        return PublishAsync(
            room,
            callback =>
            {
                callback.OnWorldState(worldState);
                return default;
            },
            cancellationToken);
    }

    public ValueTask PublishPlayerDeadAsync(RoomSnapshot room, PlayerDead playerDead, CancellationToken cancellationToken = default)
    {
        return PublishAsync(
            room,
            callback =>
            {
                callback.OnPlayerDead(playerDead);
                return default;
            },
            cancellationToken);
    }

    public ValueTask PublishMatchEndAsync(RoomSnapshot room, MatchEnd matchEnd, CancellationToken cancellationToken = default)
    {
        return PublishAsync(
            room,
            callback =>
            {
                callback.OnMatchEnd(matchEnd);
                return default;
            },
            cancellationToken);
    }

    private async ValueTask PublishAsync(
        RoomSnapshot room,
        Func<IBattleCallback, ValueTask> notify,
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
                .ForSession(realtimeSession)
                .NotifyAsync(notify, cancellationToken)
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
