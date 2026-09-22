using Server.App.Sessions;
using Server.App.Users;
using Server.App.Routing;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.Logging;
using Server.Hotfix.Matchmaking;
using Server.Hotfix.Players;
using Server.Hotfix.Users;

namespace Server.Hotfix.Sessions;

[HotfixLifecycle]
public sealed class ControlSessionLifecycle : IGameSessionLifecycle
{
    /// <inheritdoc />
    public ValueTask SessionDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call) =>
        MarkControlDisconnectedAsync(call);

    /// <inheritdoc />
    public ValueTask SessionResumedAsync(HotfixLifecycleCall<GameSessionResumedRequest> call) =>
        MarkControlResumedAsync(call);

    /// <inheritdoc />
    public ValueTask SessionExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call) =>
        MarkControlExpiredAsync(call);

    /// <summary>Restore the current control session's connection without recreating released player state.</summary>
    public async ValueTask MarkControlResumedAsync(HotfixLifecycleCall<GameSessionResumedRequest> call)
    {
        if (_actors is null || string.IsNullOrWhiteSpace(call.Request.OwnerKey)) return;
        try
        {
            await _actors.Route<UserActor>(new UserId(call.Request.OwnerKey))
                .CallAsync(static behavior => behavior.MarkControlResumedAsync,
                    new PlayerSessionResumeRequest
                    {
                        UserId = call.Request.OwnerKey,
                        SessionId = call.Request.SessionId,
                        ConnectionId = call.Request.ConnectionId
                    }).ConfigureAwait(false);
        }
        catch (ActorNotFoundException)
        {
            // Recovery must not recreate a player whose business state has already been released.
        }
    }

    private readonly ActorAccess? _actors;
    private readonly LocalActorNodeIdentity _localNode;
    private readonly ILogger<ControlSessionLifecycle> _logger;
    private readonly ILogger<PlayerService> _playerLogger;
    private readonly MatchmakingNotifier _matchmakingNotifier;

    public ControlSessionLifecycle(
        LocalActorNodeIdentity localNode,
        ILogger<ControlSessionLifecycle> logger,
        ILogger<PlayerService> playerLogger,
        MatchmakingNotifier matchmakingNotifier,
        ActorAccess? actors = null)
    {
        _actors = actors;
        _localNode = localNode;
        _logger = logger;
        _playerLogger = playerLogger;
        _matchmakingNotifier = matchmakingNotifier;
    }

    /// <summary>Clear the current control connection while retaining player state for recovery.</summary>
    public async ValueTask MarkControlDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call)
    {
        var playerId = call.Request.OwnerKey;
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        if (_actors is null)
        {
            _logger.LogWarning(
                "Cannot mark player {PlayerId} disconnected for control connection {ConnectionId} because ActorAccess is unavailable.",
                playerId,
                call.Request.ConnectionId);
            return;
        }

        try
        {
            await _actors
                .Route<UserActor>(new UserId(playerId))
                .CallAsync(
                    static behavior => behavior.MarkControlDisconnectedAsync,
                    new PlayerSessionDisconnectRequest
                    {
                        UserId = playerId,
                        ConnectionId = call.Request.ConnectionId,
                        DisconnectedAtUtc = DateTime.UtcNow,
                        Reason = "Control disconnect"
                    })
                .ConfigureAwait(false);
        }
        catch (ActorNotFoundException)
        {
            _logger.LogDebug(
                "Player {PlayerId} has no live User Actor while handling control disconnect {ConnectionId}; nothing to mark disconnected.",
                playerId,
                call.Request.ConnectionId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to mark player {PlayerId} disconnected for control connection {ConnectionId}.",
                playerId,
                call.Request.ConnectionId);
        }
    }

    /// <summary>Release matchmaking, room membership and player state when control recovery expires.</summary>
    public async ValueTask MarkControlExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call)
    {
        var playerId = call.Request.OwnerKey;
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        if (_actors is null)
        {
            _logger.LogWarning(
                "Cannot expire session {SessionId} for player {PlayerId} because ActorAccess is unavailable.",
                call.Request.SessionId,
                playerId);
            return;
        }

        try
        {
            await PlayerService
                .ReleasePlayerAsync(
                    _actors,
                    _matchmakingNotifier,
                    _localNode,
                    _playerLogger,
                    playerId,
                    "Session recovery window expired",
                    CancellationToken.None,
                    expectedControlSessionId: call.Request.SessionId)
                .ConfigureAwait(false);
        }
        catch (ActorNotFoundException)
        {
            _logger.LogDebug(
                "Player {PlayerId} has no live User Actor while expiring session {SessionId}; nothing to release.",
                playerId,
                call.Request.SessionId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to expire session {SessionId} for player {PlayerId}.",
                call.Request.SessionId,
                playerId);
        }
    }

}
