using Server.App.Rooms;
using Microsoft.Extensions.Logging;
using Shared.Interfaces;
using Lakona.Game.Server.Sessions;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.Rooms;

[HotfixComponent]
public sealed class RoomNotifier
{
    internal const int FramePushInterval = 4;
    internal const int MaxFramesPerPush = 20;

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

    public void PublishFrames(RoomSnapshot room, IReadOnlyList<FrameSyncFrame> frames)
    {
        if (frames.Count == 0 || !ShouldPublishFrame(frames[^1].Frame))
        {
            return;
        }

        foreach (var player in room.Players)
        {
            if (!TryGetRealtimeSession(player, out var session))
            {
                continue;
            }

            var missingFrames = SelectFramesAfter(frames, player.LastReceivedServerTick);
            if (missingFrames.Count > 0)
            {
                LogStatus(room.RoomId, session, _notifications.ForSession<IBattleCallback>(session)
                    .OnFrames(new FrameSyncPush { Frames = missingFrames }));
            }
        }
    }

    internal static bool ShouldPublishFrame(int frame) =>
        frame == 1 || frame % FramePushInterval == 0;

    internal static List<FrameSyncFrame> SelectFramesAfter(
        IReadOnlyList<FrameSyncFrame> frames,
        int lastReceivedServerTick)
    {
        return frames
            .Where(frame => frame.Frame > lastReceivedServerTick)
            .OrderBy(static frame => frame.Frame)
            .Take(MaxFramesPerPush)
            .ToList();
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
