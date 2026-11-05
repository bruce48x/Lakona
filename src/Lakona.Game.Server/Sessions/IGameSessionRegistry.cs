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

    /// <summary>Releases the replay hold only for the specified, still-current committed connection.</summary>
    ValueTask<bool> TryMarkReliableReplayReadyAsync(
        GameSessionKey session,
        string connectionId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Reliable session recovery requires the registry to implement connection-checked replay completion.");

    /// <summary>Atomically consumes a committed recovery notification for the current connection after replay.</summary>
    ValueTask<GameSessionSnapshot?> TakeResumedSessionAsync(
        GameSessionKey session,
        string connectionId,
        CancellationToken cancellationToken = default);

    ValueTask<GameSessionBindResult> BindSessionAsync(
        GameSessionKey session,
        string connectionId,
        CancellationToken cancellationToken = default);

    /// <summary>Prepares a replacement while retaining the previous connection's lifecycle tracking until commit or rollback.</summary>
    ValueTask<GameSessionBindResult> PrepareSessionBindingAsync(
        GameSessionKey session,
        string connectionId,
        CancellationToken cancellationToken = default);

    /// <summary>Stages the server's wire-format identity with the binding. A committed identity is immutable for this session.</summary>
    ValueTask<GameSessionBindResult> PrepareProfiledSessionBindingAsync(
        GameSessionKey session,
        string connectionId,
        string serializerProfile,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "The configured Game Session registry must implement profiled session bindings for application endpoints.");

    /// <summary>Returns the committed notification serializer profile while the session is live or resumable.</summary>
    /// <remarks>Registries without profile support return null. Encoded notifications require a known profile.</remarks>
    ValueTask<string?> GetNotificationSerializerProfileAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default) => default;

    ValueTask CommitSessionBindingAsync(
        GameSessionKey session,
        string connectionId,
        CancellationToken cancellationToken = default);

    /// <summary>Restores the previous binding's latest state, preserving intervening disconnects. Does not undo termination.</summary>
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
