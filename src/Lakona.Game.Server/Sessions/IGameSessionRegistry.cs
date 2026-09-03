using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

public interface IGameSessionRegistry
{
    ValueTask<GameSessionKey> StartNewSessionAsync(
        string ownerKey,
        CancellationToken cancellationToken = default);

    ValueTask<SessionResumeDecision> TryResumeAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default);

    ValueTask SetReliablePushPolicyAsync(
        GameSessionKey session,
        bool enabled,
        CancellationToken cancellationToken = default);

    ValueTask<bool> GetReliablePushPolicyAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default);

    ValueTask MarkReliableContinuityLostAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default);

    ValueTask<bool> IsReliableContinuityLostAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default);

    ValueTask<bool> IsReliableReplayPendingAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default);

    ValueTask MarkReliableReplayReadyAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default);

    ValueTask<GameSessionBindResult> BindSessionAsync(
        GameSessionKey session,
        string connectionId,
        CancellationToken cancellationToken = default);

    ValueTask<GameSessionBindResult> PrepareSessionBindingAsync(
        GameSessionKey session,
        string connectionId,
        CancellationToken cancellationToken = default);

    ValueTask CommitSessionBindingAsync(
        GameSessionKey session,
        string connectionId,
        CancellationToken cancellationToken = default);

    ValueTask RollbackSessionBindingAsync(
        GameSessionKey session,
        string connectionId,
        CancellationToken cancellationToken = default);

    ValueTask RemoveSessionAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default);

    ValueTask<GameSessionKey?> GetCurrentSessionAsync(
        string connectionId,
        CancellationToken cancellationToken = default);

    ValueTask<string?> GetConnectionIdAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default);

    ValueTask SetSessionItemAsync(
        GameSessionKey session,
        string key,
        GameSessionItemValue value,
        CancellationToken cancellationToken = default);

    ValueTask<GameSessionItemValue?> GetSessionItemAsync(
        GameSessionKey session,
        string key,
        CancellationToken cancellationToken = default);

    ValueTask<GameSessionItems> GetSessionItemsAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default);

    ValueTask RemoveSessionItemAsync(
        GameSessionKey session,
        string key,
        CancellationToken cancellationToken = default);

    ValueTask<GameSessionSnapshot?> MarkConnectionDisconnectedAsync(
        string connectionId,
        CancellationToken cancellationToken = default);

    ValueTask MarkSessionDisconnectedAsync(
        GameSessionKey session,
        string? connectionId = null,
        CancellationToken cancellationToken = default);

    ValueTask<GameSessionSnapshot?> MarkSessionTerminatedAsync(
        GameSessionKey session,
        SessionTerminationNotice notice,
        bool keepForResume,
        CancellationToken cancellationToken = default);

    ValueTask<GameSessionHeartbeatResult> RecordHeartbeatAsync(
        string connectionId,
        DateTimeOffset heartbeatAt,
        CancellationToken cancellationToken = default);

    GameSessionDiagnosticsSnapshot GetDiagnosticsSnapshot();

    ValueTask<IReadOnlyList<GameSessionExpiration>> ExpireSessionsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
