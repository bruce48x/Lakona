using Shared.Interfaces;
using Lakona.Game.Server;
using Lakona.Game.Server.ReliablePush;
using Microsoft.Extensions.Logging;

namespace Server.Hotfix.Services;

internal sealed class MatchmakingNotifier
{
    private readonly ILakonaGameServer _gameServer;
    private readonly PlayerSessionRegistry _playerSessionRegistry;
    private readonly ILogger<MatchmakingNotifier> _logger;

    public MatchmakingNotifier(
        ILakonaGameServer gameServer,
        PlayerSessionRegistry playerSessionRegistry,
        ILogger<MatchmakingNotifier> logger)
    {
        _gameServer = gameServer;
        _playerSessionRegistry = playerSessionRegistry;
        _logger = logger;
    }

    public async ValueTask PublishAsync(string playerId, MatchmakingStatusUpdate update, CancellationToken cancellationToken = default)
    {
        var registration = _playerSessionRegistry.Get(playerId);
        if (registration?.ControlSessionKey is not { } controlSession)
        {
            return;
        }

        await _gameServer.PublishReliablePushAsync(
            controlSession,
            PushNotificationKinds.MatchmakingStatus,
            Clone(update),
            DeliverAsync,
            cancellationToken).ConfigureAwait(false);
    }

    public ValueTask ReplayPendingAsync(string playerId, CancellationToken cancellationToken = default)
    {
        var registration = _playerSessionRegistry.Get(playerId);
        return registration?.ControlSessionKey is not { } controlSession
            ? default
            : _gameServer.ReplayReliablePushAsync(controlSession, DeliverAsync, cancellationToken);
    }

    private async ValueTask DeliverAsync(ReliablePushRecord record)
    {
        if (!string.Equals(record.Kind, PushNotificationKinds.MatchmakingStatus, StringComparison.Ordinal) ||
            record.Payload is not MatchmakingStatusUpdate update)
        {
            return;
        }

        var registration = _playerSessionRegistry.GetByReliablePushOwnerKey(record.OwnerKey);
        if (registration?.ControlSessionKey is not { } controlSession)
        {
            return;
        }

        var callback = await _gameServer.GetCallbackAsync<IControlCallback>(controlSession).ConfigureAwait(false);
        if (callback is null)
        {
            return;
        }

        var payload = Clone(update);
        payload.ReliableSequence = record.Sequence;
        SafeInvoke(callback, target => target.OnMatchmakingStatus(payload));
    }

    private void SafeInvoke(IControlCallback callback, Action<IControlCallback> action)
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
