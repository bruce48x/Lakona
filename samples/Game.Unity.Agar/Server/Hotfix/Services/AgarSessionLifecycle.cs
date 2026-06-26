using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Contracts.Users;
using Agar.Sample.State.Users;
using Agar.Sample.State.Contracts;
using Agar.Sample.State.Rooms;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Server.Hotfix.Services;
using Server.Hotfix.State.Sessions;
using Server.Hotfix.State.Rooms;
using Shared.Interfaces;

namespace Server.Hotfix.Services;

[HotfixLifecycle(typeof(IGameSessionLifecycle))]
public sealed class AgarSessionLifecycle
{
    public static async ValueTask SessionDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call)
    {
        var services = AgarLifecycleDependencies.From(call);
        var playerId = call.Request.OwnerKey;
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        if (IsRealtime(call.Request.CallbackContractTypeNames))
        {
            await ClearRealtimeStateAsync(
                    call.Services,
                    services.Logger,
                    playerId,
                    call.Request.SessionId,
                    call.Request.Generation,
                    "Realtime disconnect")
                .ConfigureAwait(false);
            return;
        }

        var users = call.Services.GetService<UserActors>();
        if (users is null)
        {
            services.Logger.LogWarning(
                "Cannot mark player {PlayerId} disconnected for control connection {ConnectionId} because UserActors is unavailable.",
                playerId,
                call.Request.ConnectionId);
            return;
        }

        try
        {
            await users
                .Get(new UserId(playerId))
                .MarkDisconnectedAsync(new PlayerSessionDisconnectRequest
                    {
                        UserId = playerId,
                        ConnectionId = call.Request.ConnectionId,
                        DisconnectedAtUtc = DateTime.UtcNow,
                        Reason = "Control disconnect"
                    })
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            services.Logger.LogError(
                ex,
                "Failed to mark player {PlayerId} disconnected for control connection {ConnectionId}.",
                playerId,
                call.Request.ConnectionId);
        }
    }

    public static async ValueTask SessionExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call)
    {
        var lifecycleServices = AgarLifecycleDependencies.From(call);
        var playerId = call.Request.OwnerKey;
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        var users = call.Services.GetService<UserActors>();
        if (users is null)
        {
            lifecycleServices.Logger.LogWarning(
                "Cannot expire session {SessionId}/{Generation} for player {PlayerId} because UserActors is unavailable.",
                call.Request.SessionId,
                call.Request.Generation,
                playerId);
            return;
        }

        var expiredSession = new GameSessionKey(
            call.Request.OwnerKey,
            call.Request.SessionId,
            call.Request.Generation);
        var snapshot = await users
            .Get(new UserId(playerId))
            .GetSnapshotAsync(new PlayerSessionSnapshotRequest())
            .ConfigureAwait(false);

        if (MatchesRealtimeSession(snapshot, expiredSession))
        {
            await ClearRealtimeStateAsync(
                    call.Services,
                    lifecycleServices.Logger,
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

        var services = AgarServiceDependencies.From(call);
        await PlayerService
            .ReleasePlayerAsync(services, playerId, "Reconnect grace period expired")
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

        var users = services.GetService<UserActors>();
        if (users is null)
        {
            return;
        }

        try
        {
            var snapshot = await users
                .Get(new UserId(playerId))
                .GetSnapshotAsync(new PlayerSessionSnapshotRequest())
                .ConfigureAwait(false);
            var realtimeSession = new GameSessionKey(playerId, sessionId, generation);
            if (!MatchesRealtimeSession(snapshot, realtimeSession))
            {
                return;
            }

            await users
                .Get(new UserId(playerId))
                .ClearRealtimeAsync(new PlayerRealtimeClearRequest
                    {
                        UserId = playerId,
                        RealtimeSessionId = sessionId,
                        RealtimeSessionGeneration = generation,
                        ClearedAtUtc = DateTime.UtcNow,
                        Reason = reason
                    })
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(snapshot.CurrentRoomId) &&
                services.GetService<RoomActors>() is { } rooms)
            {
                await rooms
                    .Get(new RoomId(snapshot.CurrentRoomId))
                    .ClearRealtimeAsync(new RoomRealtimeClearRequest
                        {
                            UserId = playerId,
                            RoomId = snapshot.CurrentRoomId,
                            RealtimeSessionId = sessionId,
                            RealtimeSessionGeneration = generation,
                            ClearedAtUtc = DateTime.UtcNow,
                            Reason = reason
                        })
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

    private static bool IsRealtime(IReadOnlyList<string> callbackContractTypeNames)
    {
        return callbackContractTypeNames.Any(static name =>
            string.Equals(name, typeof(IBattleCallback).FullName, StringComparison.Ordinal));
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

internal sealed record AgarLifecycleDependencies(
    ILogger<AgarSessionLifecycle> Logger)
{
    public static AgarLifecycleDependencies From<TRequest>(HotfixLifecycleCall<TRequest> call)
    {
        var loggerFactory = call.Services.GetRequiredService<ILoggerFactory>();
        return new AgarLifecycleDependencies(
            loggerFactory.CreateLogger<AgarSessionLifecycle>());
    }
}
