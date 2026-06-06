using Shared.Interfaces;
using Lakona.Game.Server.ReliablePush;
using Microsoft.Extensions.Logging;

namespace Gateway.Services;

internal sealed class ReliableMatchmakingPublisher
{
    private readonly IReliablePushOutbox _reliablePushOutbox;
    private readonly SessionDirectory _sessionDirectory;
    private readonly ILogger<ReliableMatchmakingPublisher> _logger;

    public ReliableMatchmakingPublisher(
        IReliablePushOutbox reliablePushOutbox,
        SessionDirectory sessionDirectory,
        ILogger<ReliableMatchmakingPublisher> logger)
    {
        _reliablePushOutbox = reliablePushOutbox;
        _sessionDirectory = sessionDirectory;
        _logger = logger;
    }

    public async ValueTask PublishAsync(string playerId, MatchmakingStatusUpdate update, CancellationToken cancellationToken = default)
    {
        var registration = _sessionDirectory.Get(playerId);
        if (registration is null)
        {
            return;
        }

        await _reliablePushOutbox.PublishAsync(
            registration.SessionKey,
            ReliablePushKinds.MatchmakingStatus,
            Clone(update),
            DeliverAsync,
            cancellationToken).ConfigureAwait(false);
    }

    public ValueTask ReplayPendingAsync(string playerId, CancellationToken cancellationToken = default)
    {
        var registration = _sessionDirectory.Get(playerId);
        return registration is null
            ? ValueTask.CompletedTask
            : _reliablePushOutbox.ReplayPendingAsync(registration.SessionKey, DeliverAsync, cancellationToken);
    }

    private async ValueTask DeliverAsync(ReliablePushRecord record)
    {
        if (!string.Equals(record.Kind, ReliablePushKinds.MatchmakingStatus, StringComparison.Ordinal) ||
            record.Payload is not MatchmakingStatusUpdate update)
        {
            return;
        }

        var registration = _sessionDirectory.GetByReliablePushOwnerKey(record.OwnerKey);
        if (registration is null)
        {
            return;
        }

        var callback = await _sessionDirectory.GetControlCallbackAsync(registration).ConfigureAwait(false);
        if (callback is null)
        {
            return;
        }

        var payload = Clone(update);
        payload.ReliableSequence = record.Sequence;
        SafeInvoke(callback, target => target.OnMatchmakingStatus(payload));
    }

    private void SafeInvoke(IPlayerCallback callback, Action<IPlayerCallback> action)
    {
        try
        {
            action(callback);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push reliable matchmaking callback.");
        }
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
