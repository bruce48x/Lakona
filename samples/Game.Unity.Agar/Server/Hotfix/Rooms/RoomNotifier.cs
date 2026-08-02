using Server.App.Rooms;
using Microsoft.Extensions.Logging;
using Shared.Interfaces;
using Lakona.Game.Server.Sessions;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.Rooms;

[HotfixComponent]
public sealed class RoomNotifier
{
    private readonly IClientNotifications _notifications;
    private readonly ILogger<RoomNotifier> _logger;

    public RoomNotifier(IClientNotifications notifications, ILogger<RoomNotifier> logger)
    {
        _notifications = notifications;
        _logger = logger;
    }

    public void PublishFrameSyncStarted(RoomSnapshot room, FrameSyncStart start)
    {
        foreach (var player in room.Players)
        {
            if (TryGetRealtimeSession(player, out var session))
            {
                LogStatus(room.RoomId, session, _notifications.ForSession<IBattleCallback>(session)
                    .OnFrameSyncStarted(start));
            }
        }
    }

    public void PublishFrame(RoomSnapshot room, FrameSyncFrame frame)
    {
        foreach (var player in room.Players)
        {
            if (TryGetRealtimeSession(player, out var session))
            {
                LogStatus(room.RoomId, session, _notifications.ForSession<IBattleCallback>(session)
                    .OnFrame(frame));
            }
        }
    }

    public void PublishMatchProgress(
        RoomSnapshot room,
        MatchProgressUpdate update)
    {
        foreach (var player in room.Players)
        {
            if (string.IsNullOrWhiteSpace(player.ControlSessionId))
            {
                continue;
            }

            var controlSession = new GameSessionKey(
                player.UserId,
                player.ControlSessionId);
            var status = _notifications
                .ForSession<IPlayerCallback>(controlSession)
                .OnMatchProgress(update);
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
        if (string.IsNullOrWhiteSpace(player.RealtimeSessionId))
        {
            session = default;
            return false;
        }

        session = new GameSessionKey(
            player.UserId,
            player.RealtimeSessionId);
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
