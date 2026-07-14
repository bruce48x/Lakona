using Shared.Interfaces;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.Logging;

namespace Server.Hotfix.Services;

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

    public async ValueTask PublishAsync(GameSessionKey controlSession, MatchmakingStatusUpdate update, CancellationToken cancellationToken = default)
    {
        var status = await _notifications
            .ForSession<IPlayerCallback>(controlSession)
            .OnMatchmakingStatus(Clone(update), cancellationToken)
            .ConfigureAwait(false);

        if (status == ClientNotificationStatus.Delivered)
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
