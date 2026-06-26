using Shared.Interfaces;
using Lakona.Game.Server;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.Logging;

namespace Server.Hotfix.Services;

internal sealed class MatchmakingNotifier
{
    private readonly ILakonaGameServer _gameServer;
    private readonly IClientNotificationRelay _notifications;
    private readonly ILogger<MatchmakingNotifier> _logger;

    public MatchmakingNotifier(
        ILakonaGameServer gameServer,
        IClientNotificationRelay notifications,
        ILogger<MatchmakingNotifier> logger)
    {
        _gameServer = gameServer;
        _notifications = notifications;
        _logger = logger;
    }

    public async ValueTask PublishAsync(GameSessionKey controlSession, MatchmakingStatusUpdate update, CancellationToken cancellationToken = default)
    {
        await _gameServer.PublishReliablePushAsync(
            controlSession,
            PushNotificationKinds.MatchmakingStatus,
            Clone(update),
            record => DeliverAsync(controlSession, record, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    public ValueTask ReplayPendingAsync(GameSessionKey controlSession, CancellationToken cancellationToken = default)
    {
        return _gameServer.ReplayReliablePushAsync(
            controlSession,
            record => DeliverAsync(controlSession, record, cancellationToken),
            cancellationToken);
    }

    private async ValueTask DeliverAsync(GameSessionKey controlSession, ReliablePushRecord record, CancellationToken cancellationToken)
    {
        if (!string.Equals(record.Kind, PushNotificationKinds.MatchmakingStatus, StringComparison.Ordinal) ||
            record.Payload is not MatchmakingStatusUpdate update)
        {
            return;
        }

        var payload = Clone(update);
        payload.ReliableSequence = record.Sequence;
        var status = await _notifications
            .NotifyAsync<IControlCallback>(
                controlSession,
                target => target.OnMatchmakingStatus(payload),
                cancellationToken)
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
            ReliableSequence = source.ReliableSequence,
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
