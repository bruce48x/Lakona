using Server.App.Sessions;
using Server.App.Users;
using Server.App.Routing;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.Logging;
using Server.Hotfix.Users;

namespace Server.Hotfix.Sessions;

[HotfixLifecycle]
public sealed class RealtimeSessionLifecycle : IGameSessionLifecycle
{
    private readonly ActorAccess _actors;
    private readonly ILogger<RealtimeSessionLifecycle> _logger;

    public RealtimeSessionLifecycle(ActorAccess actors, ILogger<RealtimeSessionLifecycle> logger)
    {
        _actors = actors;
        _logger = logger;
    }

    /// <inheritdoc />
    public ValueTask SessionDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call) =>
        MarkRealtimeDisconnectedAsync(call);

    /// <inheritdoc />
    public ValueTask SessionResumedAsync(HotfixLifecycleCall<GameSessionResumedRequest> call) =>
        MarkRealtimeResumedAsync(call);

    /// <inheritdoc />
    public ValueTask SessionExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call) =>
        MarkRealtimeExpiredAsync(call);

    /// <summary>Retain room membership and readiness while the realtime session can recover.</summary>
    public ValueTask MarkRealtimeDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call) => default;

    /// <summary>Continue with the retained room state; no new join or ready operation is needed.</summary>
    public ValueTask MarkRealtimeResumedAsync(HotfixLifecycleCall<GameSessionResumedRequest> call) => default;

    /// <summary>Clear expired realtime state in the player and room, preserving the control session.</summary>
    public ValueTask MarkRealtimeExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call) =>
        ClearRealtimeStateAsync(call.Request.OwnerKey, call.Request.SessionId, "Realtime session expired");

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

        try
        {
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
        }
        catch (ActorNotFoundException)
        {
            _logger.LogDebug(
                "Player {PlayerId} or the room no longer exists while clearing realtime session {SessionId}; nothing to clear.",
                playerId,
                sessionId);
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

}
