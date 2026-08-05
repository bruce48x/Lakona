using Lakona.Game.Abstractions;
using Lakona.Game.Server.Sessions;

namespace Lakona.Game.Server;

/// <summary>
/// Provides the high-level server API for framework-owned game sessions.
/// </summary>
/// <remarks>
/// Use this service from game services and hotfix services to create sessions,
/// bind connections, and terminate sessions. Framework reconnect recovery is
/// owned by the game handshake and is not a business API.
/// Game code still owns account identity, matchmaking, room state, and gameplay
/// policy; this interface owns the framework session-to-connection binding.
/// </remarks>
public interface ILakonaGameServer
{
    /// <summary>
    /// Creates a new game session for an owner without binding it to a connection.
    /// </summary>
    /// <param name="ownerKey">
    /// Stable game-owned owner identity, such as a player id or account id.
    /// </param>
    /// <param name="cancellationToken">A token that cancels session creation.</param>
    /// <returns>The framework session key assigned to the new game session.</returns>
    /// <remarks>
    /// Use this overload when the connection will be bound later with
    /// <see cref="BindSessionAsync(GameSessionKey, string, CancellationToken)"/>.
    /// </remarks>
    ValueTask<GameSessionKey> StartSessionAsync(
        string ownerKey,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a game session and binds it to the current RPC connection.</summary>
    ValueTask<GameSessionKey> StartSessionAsync(
        string ownerKey,
        string connectionId,
        CancellationToken cancellationToken = default);

    ValueTask BindSessionAsync(
        GameSessionKey session,
        string connectionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a game session or one of its connections as disconnected without terminating the session.
    /// </summary>
    /// <param name="session">The session whose current connection state changed.</param>
    /// <param name="connectionId">
    /// Optional connection id to mark disconnected. When omitted, the session is
    /// marked disconnected regardless of the last bound connection id.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the disconnect update.</param>
    /// <remarks>
    /// A disconnected session remains eligible for reconnect/resume until cleanup
    /// policy expires it or the game explicitly terminates it.
    /// </remarks>
    ValueTask MarkSessionDisconnectedAsync(
        GameSessionKey session,
        string? connectionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets server-side local metadata for a game session.
    /// </summary>
    /// <param name="session">The session that owns the metadata item.</param>
    /// <param name="key">The case-sensitive metadata key.</param>
    /// <param name="value">The metadata value to store.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <remarks>
    /// Session items are process-local framework metadata, not durable business
    /// state. They are cleared when the session terminates or expires.
    /// </remarks>
    ValueTask SetSessionItemAsync(
        GameSessionKey session,
        string key,
        GameSessionItemValue value,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one server-side local metadata item for a game session.
    /// </summary>
    /// <param name="session">The session that owns the metadata item.</param>
    /// <param name="key">The case-sensitive metadata key.</param>
    /// <param name="cancellationToken">A token that cancels the lookup.</param>
    /// <returns>The item value when present on an active session; otherwise <see langword="null"/>.</returns>
    /// <remarks>
    /// Session items are process-local framework metadata, not durable business
    /// state. They are cleared when the session terminates or expires.
    /// </remarks>
    ValueTask<GameSessionItemValue?> GetSessionItemAsync(
        GameSessionKey session,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a snapshot of all server-side local metadata for a game session.
    /// </summary>
    /// <param name="session">The session whose metadata should be read.</param>
    /// <param name="cancellationToken">A token that cancels the lookup.</param>
    /// <returns>An immutable snapshot of items, or an empty snapshot when none are available.</returns>
    /// <remarks>
    /// Session items are process-local framework metadata, not durable business
    /// state. They are cleared when the session terminates or expires.
    /// </remarks>
    ValueTask<GameSessionItems> GetSessionItemsAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one server-side local metadata item from a game session.
    /// </summary>
    /// <param name="session">The session that owns the metadata item.</param>
    /// <param name="key">The case-sensitive metadata key.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <remarks>
    /// Session items are process-local framework metadata, not durable business
    /// state. They are cleared when the session terminates or expires.
    /// </remarks>
    ValueTask RemoveSessionItemAsync(
        GameSessionKey session,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Terminates a game session and optionally notifies the bound client before closing its connection.
    /// </summary>
    /// <param name="session">The session to terminate.</param>
    /// <param name="reason">The machine-readable termination reason sent to the client.</param>
    /// <param name="message">Optional human-readable diagnostic text for logs or client UI.</param>
    /// <param name="options">Termination behavior such as notify timeout and resume-state retention.</param>
    /// <param name="cancellationToken">A token that cancels the termination operation before notification begins.</param>
    /// <remarks>
    /// Termination is final for the current session. Depending on
    /// <see cref="SessionTerminationOptions.KeepTerminalStateForResume"/>, later
    /// resume attempts can receive a terminated decision instead of a generic
    /// state-lost result.
    /// </remarks>
    ValueTask TerminateSessionAsync(
        GameSessionKey session,
        SessionTerminationReason reason,
        string? message = null,
        SessionTerminationOptions? options = null,
        CancellationToken cancellationToken = default);
}
