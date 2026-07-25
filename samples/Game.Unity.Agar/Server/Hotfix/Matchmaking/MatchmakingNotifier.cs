using Shared.Interfaces;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.Logging;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.Matchmaking;

[HotfixComponent]
public sealed class MatchmakingNotifier
{
    private readonly IClientNotifications _notifications;
    private readonly ILogger<MatchmakingNotifier> _logger;

    public MatchmakingNotifier(
        IClientNotifications notifications,
        ILogger<MatchmakingNotifier> logger)
    {
        _notifications = notifications;
        _logger = logger;
    }

    public void Publish(GameSessionKey controlSession, MatchmakingStatusUpdate update)
    {
        var status = _notifications
            .ForSession<IPlayerCallback>(controlSession)
            .OnMatchmakingStatus(Clone(update));

        if (status == ClientNotificationStatus.Accepted)
        {
            return;
        }

        _logger.LogDebug(
            "Matchmaking notification delivery returned {Status} for session {Session}.",
            status,
            controlSession);
    }

    private static MatchmakingStatusUpdate Clone(MatchmakingStatusUpdate source)
    {
        return new MatchmakingStatusUpdate
        {
            State = source.State,
            Message = source.Message,
            RoomId = source.RoomId,
            QueuePosition = source.QueuePosition,
            QueueSize = source.QueueSize,
            RoomCapacity = source.RoomCapacity,
            MatchedPlayerCount = source.MatchedPlayerCount,
            RealtimeConnection = source.RealtimeConnection is null
                ? null
                : new RealtimeConnectionInfo
                {
                    Transport = source.RealtimeConnection.Transport,
                    Host = source.RealtimeConnection.Host,
                    Port = source.RealtimeConnection.Port,
                    Path = source.RealtimeConnection.Path,
                    RoomId = source.RealtimeConnection.RoomId,
                    MatchId = source.RealtimeConnection.MatchId,
                    SessionToken = source.RealtimeConnection.SessionToken
                }
        };
    }
}
