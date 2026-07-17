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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Server.Hotfix.Services;
using Server.Hotfix.State.Users;
using Server.Hotfix.State.Rooms;
using Shared.Interfaces;

namespace Server.Hotfix.Services;

[HotfixLifecycle(typeof(IGameSessionLifecycle))]
public sealed class AgarSessionLifecycle
{
    public async ValueTask SessionDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call)
    {
        var logger = call.Services.GetRequiredService<ILogger<AgarSessionLifecycle>>();
        var playerId = call.Request.OwnerKey;
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        var actors = call.Services.GetService<ActorAccess>();
        if (actors is null)
        {
            logger.LogWarning(
                "Cannot mark player {PlayerId} disconnected for control connection {ConnectionId} because ActorAccess is unavailable.",
                playerId,
                call.Request.ConnectionId);
            return;
        }

        try
        {
            var disconnectedSession = new GameSessionKey(
                call.Request.OwnerKey,
                call.Request.SessionId,
                call.Request.Generation);
            var snapshot = await actors
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

            await actors
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
            logger.LogError(
                ex,
                "Failed to mark player {PlayerId} disconnected for control connection {ConnectionId}.",
                playerId,
                call.Request.ConnectionId);
        }
    }

    public async ValueTask SessionExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call)
    {
        var logger = call.Services.GetRequiredService<ILogger<AgarSessionLifecycle>>();
        var playerId = call.Request.OwnerKey;
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        var actors = call.Services.GetService<ActorAccess>();
        if (actors is null)
        {
            logger.LogWarning(
                "Cannot expire session {SessionId}/{Generation} for player {PlayerId} because ActorAccess is unavailable.",
                call.Request.SessionId,
                call.Request.Generation,
                playerId);
            return;
        }

        var expiredSession = new GameSessionKey(
            call.Request.OwnerKey,
            call.Request.SessionId,
            call.Request.Generation);
        var snapshot = await actors
            .Route<UserActor>(new UserId(playerId))
            .CallAsync(
                static behavior => behavior.GetSnapshotAsync,
                new PlayerSessionSnapshotRequest(),
                CancellationToken.None)
            .ConfigureAwait(false);

        if (MatchesRealtimeSession(snapshot, expiredSession))
        {
            await ClearRealtimeStateAsync(
                    call.Services,
                    logger,
                    playerId,
                    expiredSession.SessionId,
                    expiredSession.Generation,
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
                actors,
                HotfixNotificationServices.GetMatchmakingNotifier(call.Services),
                call.Services.GetRequiredService<LocalActorNodeIdentity>(),
                call.Services.GetRequiredService<ILogger<PlayerService>>(),
                playerId,
                "Session recovery window expired",
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static async ValueTask ClearRealtimeStateAsync(
        IServiceProvider services,
        ILogger<AgarSessionLifecycle> logger,
        string playerId,
        string sessionId,
        long generation,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(playerId) ||
            string.IsNullOrWhiteSpace(sessionId) ||
            generation <= 0)
        {
            return;
        }

        var actors = services.GetService<ActorAccess>();
        if (actors is null)
        {
            return;
        }

        try
        {
            var snapshot = await actors
                .Route<UserActor>(new UserId(playerId))
                .CallAsync(
                    static behavior => behavior.GetSnapshotAsync,
                    new PlayerSessionSnapshotRequest(),
                    CancellationToken.None)
                .ConfigureAwait(false);
            var realtimeSession = new GameSessionKey(playerId, sessionId, generation);
            if (!MatchesRealtimeSession(snapshot, realtimeSession))
            {
                return;
            }

            await actors
                .Route<UserActor>(new UserId(playerId))
                .CallAsync(
                    static behavior => behavior.ClearRealtimeAsync,
                    new PlayerRealtimeClearRequest
                    {
                        UserId = playerId,
                        RealtimeSessionId = sessionId,
                        RealtimeSessionGeneration = generation,
                        ClearedAtUtc = DateTime.UtcNow,
                        Reason = reason
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(snapshot.CurrentRoomId))
            {
                await actors
                    .Route<RoomActor>(new RoomId(snapshot.CurrentRoomId))
                    .CallAsync(
                        static behavior => behavior.ClearRealtimeAsync,
                        new RoomRealtimeClearRequest
                        {
                            UserId = playerId,
                            RoomId = snapshot.CurrentRoomId,
                            RealtimeSessionId = sessionId,
                            RealtimeSessionGeneration = generation,
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
            logger.LogError(
                ex,
                "Failed to clear realtime session {SessionId}/{Generation} for player {PlayerId}.",
                sessionId,
                generation,
                playerId);
        }
    }

    private static bool MatchesControlSession(PlayerSessionSnapshot snapshot, GameSessionKey session)
    {
        return string.Equals(snapshot.UserId, session.OwnerKey, StringComparison.Ordinal) &&
            string.Equals(snapshot.ControlSessionId, session.SessionId, StringComparison.Ordinal) &&
            snapshot.ControlSessionGeneration == session.Generation;
    }

    private static bool MatchesRealtimeSession(PlayerSessionSnapshot snapshot, GameSessionKey session)
    {
        return string.Equals(snapshot.UserId, session.OwnerKey, StringComparison.Ordinal) &&
            string.Equals(snapshot.RealtimeSessionId, session.SessionId, StringComparison.Ordinal) &&
            snapshot.RealtimeSessionGeneration == session.Generation;
    }
}
