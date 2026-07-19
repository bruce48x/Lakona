using Server.App.State.Contracts.Rooms;
using Server.App.State.Contracts.Sessions;
using Server.App.State.Contracts.Users;
using Server.App.State.Users;
using Server.App.State.Contracts;
using Server.App.State.Matchmaking;
using Server.App.State.Rooms;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.Logging;
using Server.Hotfix.Services;
using Server.Hotfix.State.Users;
using Server.Hotfix.State.Rooms;
using Shared.Interfaces;

namespace Server.Hotfix.Services;

[HotfixLifecycle(typeof(IGameSessionLifecycle))]
public sealed class AgarSessionLifecycle
{
    private readonly ActorAccess? _actors;
    private readonly LocalActorNodeIdentity _localNode;
    private readonly ILogger<AgarSessionLifecycle> _logger;
    private readonly ILogger<PlayerService> _playerLogger;
    private readonly MatchmakingNotifier _matchmakingNotifier;

    public AgarSessionLifecycle(
        LocalActorNodeIdentity localNode,
        ILogger<AgarSessionLifecycle> logger,
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

    public async ValueTask SessionDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call)
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
            var disconnectedSession = new GameSessionKey(
                call.Request.OwnerKey,
                call.Request.SessionId);
            var snapshot = await _actors
                .Route<UserActor>(new UserId(playerId))
                .CallAsync(
                    static behavior => behavior.GetSnapshotAsync,
                    new PlayerSessionSnapshotRequest(),
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (MatchesRealtimeSession(snapshot, disconnectedSession) ||
                !MatchesControlSession(snapshot, disconnectedSession))
            {
                // A realtime transport disconnect remains resumable. Stale sessions
                // must not mutate the player's current control presence either.
                return;
            }

            await _actors
                .Route<UserActor>(new UserId(playerId))
                .CallAsync(
                    static behavior => behavior.MarkDisconnectedAsync,
                    new PlayerSessionDisconnectRequest
                    {
                        UserId = playerId,
                        ConnectionId = call.Request.ConnectionId,
                        DisconnectedAtUtc = DateTime.UtcNow,
                        Reason = "Control disconnect"
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
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

    public async ValueTask SessionExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call)
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

        var expiredSession = new GameSessionKey(
            call.Request.OwnerKey,
            call.Request.SessionId);
        var snapshot = await _actors
            .Route<UserActor>(new UserId(playerId))
            .CallAsync(
                static behavior => behavior.GetSnapshotAsync,
                new PlayerSessionSnapshotRequest(),
                CancellationToken.None)
            .ConfigureAwait(false);

        if (MatchesRealtimeSession(snapshot, expiredSession))
        {
            await ClearRealtimeStateAsync(
                    playerId,
                    expiredSession.SessionId,
                    "Realtime session expired")
                .ConfigureAwait(false);
            return;
        }

        if (!MatchesControlSession(snapshot, expiredSession))
        {
            return;
        }

        await PlayerService
            .ReleasePlayerAsync(
                _actors,
                _matchmakingNotifier,
                _localNode,
                _playerLogger,
                playerId,
                "Session recovery window expired",
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private async ValueTask ClearRealtimeStateAsync(
        string playerId,
        string sessionId,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(playerId) ||
            string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        if (_actors is null)
        {
            return;
        }

        try
        {
            var snapshot = await _actors
                .Route<UserActor>(new UserId(playerId))
                .CallAsync(
                    static behavior => behavior.GetSnapshotAsync,
                    new PlayerSessionSnapshotRequest(),
                    CancellationToken.None)
                .ConfigureAwait(false);
            var realtimeSession = new GameSessionKey(playerId, sessionId);
            if (!MatchesRealtimeSession(snapshot, realtimeSession))
            {
                return;
            }

            await _actors
                .Route<UserActor>(new UserId(playerId))
                .CallAsync(
                    static behavior => behavior.ClearRealtimeAsync,
                    new PlayerRealtimeClearRequest
                    {
                        UserId = playerId,
                        RealtimeSessionId = sessionId,
                        ClearedAtUtc = DateTime.UtcNow,
                        Reason = reason
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(snapshot.CurrentRoomId))
            {
                await _actors
                    .Route<RoomActor>(new RoomId(snapshot.CurrentRoomId))
                    .CallAsync(
                        static behavior => behavior.ClearRealtimeAsync,
                        new RoomRealtimeClearRequest
                        {
                            UserId = playerId,
                            RoomId = snapshot.CurrentRoomId,
                            RealtimeSessionId = sessionId,
                            ClearedAtUtc = DateTime.UtcNow,
                            Reason = reason
                        },
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to clear realtime session {SessionId} for player {PlayerId}.",
                sessionId,
                playerId);
        }
    }

    private static bool MatchesControlSession(PlayerSessionSnapshot snapshot, GameSessionKey session)
    {
        return string.Equals(snapshot.UserId, session.OwnerKey, StringComparison.Ordinal) &&
            string.Equals(snapshot.ControlSessionId, session.SessionId, StringComparison.Ordinal);
    }

    private static bool MatchesRealtimeSession(PlayerSessionSnapshot snapshot, GameSessionKey session)
    {
        return string.Equals(snapshot.UserId, session.OwnerKey, StringComparison.Ordinal) &&
            string.Equals(snapshot.RealtimeSessionId, session.SessionId, StringComparison.Ordinal);
    }
}
