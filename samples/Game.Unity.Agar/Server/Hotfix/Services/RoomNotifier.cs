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

    public async ValueTask PublishWorldStateAsync(RoomSnapshot room, WorldState worldState, CancellationToken cancellationToken = default)
    {
        foreach (var player in room.Players)
        {
            if (TryGetRealtimeSession(player, out var session))
            {
                LogStatus(room.RoomId, session, await _notifications.ForSession<IBattleCallback>(session)
                    .OnWorldState(worldState, cancellationToken).ConfigureAwait(false));
            }
        }
    }

    public async ValueTask PublishPlayerDeadAsync(RoomSnapshot room, PlayerDead playerDead, CancellationToken cancellationToken = default)
    {
        foreach (var player in room.Players)
        {
            if (TryGetRealtimeSession(player, out var session))
            {
                LogStatus(room.RoomId, session, await _notifications.ForSession<IBattleCallback>(session)
                    .OnPlayerDead(playerDead, cancellationToken).ConfigureAwait(false));
            }
        }
    }

    public async ValueTask PublishMatchEndAsync(RoomSnapshot room, MatchEnd matchEnd, CancellationToken cancellationToken = default)
    {
        foreach (var player in room.Players)
        {
            if (TryGetRealtimeSession(player, out var session))
            {
                LogStatus(room.RoomId, session, await _notifications.ForSession<IBattleCallback>(session)
                    .OnMatchEnd(matchEnd, cancellationToken).ConfigureAwait(false));
            }
        }
    }

    public async ValueTask PublishMatchProgressAsync(
        RoomSnapshot room,
        MatchProgressUpdate update,
        CancellationToken cancellationToken = default)
    {
        foreach (var player in room.Players)
        {
            if (string.IsNullOrWhiteSpace(player.ControlSessionId) ||
                player.ControlSessionGeneration <= 0)
            {
                continue;
            }

            var controlSession = new GameSessionKey(
                player.UserId,
                player.ControlSessionId,
                player.ControlSessionGeneration);
            var status = await _notifications
                .ForSession<IPlayerCallback>(controlSession)
                .OnMatchProgress(update, cancellationToken)
                .ConfigureAwait(false);
            if (status != ClientNotificationStatus.Accepted &&
                status != ClientNotificationStatus.CallbackUnavailable)
            {
                _logger.LogDebug(
                    "Match progress publication returned {Status} for room {RoomId}.",
                    status,
                    room.RoomId);
            }
        }
    }

    private static bool TryGetRealtimeSession(RoomPlayerSnapshot player, out GameSessionKey session)
    {
        if (string.IsNullOrWhiteSpace(player.RealtimeSessionId) ||
            player.RealtimeSessionGeneration <= 0)
        {
            session = default;
            return false;
        }

        session = new GameSessionKey(
            player.UserId,
            player.RealtimeSessionId,
            player.RealtimeSessionGeneration);
        return true;
    }

    private void LogStatus(
        string roomId,
        GameSessionKey session,
        ClientNotificationStatus status)
    {
        if (status != ClientNotificationStatus.Accepted)
        {
            _logger.LogDebug(
                "Room notification delivery returned {Status} for room {RoomId} session {Session}.",
                status,
                roomId,
                session);
        }

    }
}
