using Lakona.Game.Abstractions;
using Lakona.Game.Server.Sessions;

namespace Lakona.Game.Server;

/// <summary>
/// Provides the high-level server API for framework-owned game sessions.
/// </summary>
/// <remarks>
/// Use this service from game services and hotfix services to create sessions,
/// bind connections, handle reconnects, and terminate sessions.
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
    /// Use this overload when the connection or callback will be bound later with
    /// <see cref="BindSessionAsync{TCallback}"/>.
    /// </remarks>
    ValueTask<GameSessionKey> StartSessionAsync(
        string ownerKey,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a game session and binds it to the current RPC connection.</summary>
    ValueTask<GameSessionKey> StartSessionAsync(
        string ownerKey,
        string connectionId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This ILakonaGameServer implementation does not support connection-only session binding.");

    /// <summary>
    /// Creates a new game session and binds the current connection callback to it.
    /// </summary>
    /// <typeparam name="TCallback">The typed client callback contract implemented by the connected client.</typeparam>
    /// <param name="ownerKey">
    /// Stable game-owned owner identity, such as a player id or account id.
    /// </param>
    /// <param name="connectionId">The framework connection id associated with the connected client.</param>
    /// <param name="callback">The callback proxy for the connected client.</param>
    /// <param name="cancellationToken">A token that cancels session creation or callback binding.</param>
    /// <returns>The framework session key assigned to the new game session.</returns>
    /// <remarks>
    /// This is the normal login or enter-game path when the server accepts a
    /// connection and immediately associates it with a new game session.
    /// </remarks>
    [Obsolete("Callbacks are resolved from the current RPC connection. Use StartSessionAsync(ownerKey, connectionId, cancellationToken).")]
    ValueTask<GameSessionKey> StartSessionAsync<TCallback>(
        string ownerKey,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    /// <summary>
    /// Attempts to resume an existing game session and bind a fresh connection callback.
    /// </summary>
    /// <typeparam name="TCallback">The typed client callback contract implemented by the connected client.</typeparam>
    /// <param name="request">The session key and optional resume token supplied by the client.</param>
    /// <param name="connectionId">The framework connection id for the reconnecting client.</param>
    /// <param name="callback">The callback proxy for the reconnecting client.</param>
    /// <param name="cancellationToken">A token that cancels resume validation or callback binding.</param>
    /// <returns>
    /// The resume decision. Successful or state-refresh decisions include the
    /// session that was rebound to <paramref name="connectionId"/>.
    /// </returns>
    /// <remarks>
    /// The method binds the callback only when the resume policy returns
    /// <see cref="SessionResumeStatus.Resumed"/> or
    /// <see cref="SessionResumeStatus.StateRefreshRequired"/>.
    /// </remarks>
    [Obsolete("Callbacks are resolved from the current RPC connection.")]
    ValueTask<SessionResumeDecision> ResumeSessionAsync<TCallback>(
        GameSessionResumeRequest request,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask<SessionResumeDecision> ResumeSessionAsync(
        GameSessionResumeRequest request,
        string connectionId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This ILakonaGameServer implementation does not support connection-only session resume.");

    /// <summary>
    /// Binds a known game session to a connection and typed client callback.
    /// </summary>
    /// <typeparam name="TCallback">The typed client callback contract implemented by the connected client.</typeparam>
    /// <param name="session">The session to associate with the connection.</param>
    /// <param name="connectionId">The framework connection id associated with the connected client.</param>
    /// <param name="callback">The callback proxy for the connected client.</param>
    /// <param name="cancellationToken">A token that cancels callback binding.</param>
    /// <remarks>
    /// Use this when the server already knows the exact <see cref="GameSessionKey"/>.
    /// Binding a session also registers the framework route used by server-to-client
    /// notifications.
    /// </remarks>
    [Obsolete("Callbacks are resolved from the current RPC connection. Use BindSessionAsync(session, connectionId, cancellationToken).")]
    ValueTask BindSessionAsync<TCallback>(
        GameSessionKey session,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask BindSessionAsync(
        GameSessionKey session,
        string connectionId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This ILakonaGameServer implementation does not support connection-only session binding.");

    /// <summary>
    /// Binds the session currently associated with a connection to a typed client callback.
    /// </summary>
    /// <typeparam name="TCallback">The typed client callback contract implemented by the connected client.</typeparam>
    /// <param name="connectionId">The framework connection id whose current session should be rebound.</param>
    /// <param name="callback">The callback proxy for the connected client.</param>
    /// <param name="cancellationToken">A token that cancels callback binding.</param>
    /// <remarks>
    /// This is useful inside RPC services that receive a framework connection id but
    /// do not need to parse or pass the full <see cref="GameSessionKey"/>.
    /// </remarks>
    [Obsolete("Callbacks are resolved from the current RPC connection; no callback binding is required.")]
    ValueTask BindCurrentSessionAsync<TCallback>(
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class;

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
    /// Gets the callback currently bound to a game session.
    /// </summary>
    /// <typeparam name="TCallback">The expected typed client callback contract.</typeparam>
    /// <param name="session">The session whose callback should be returned.</param>
    /// <param name="cancellationToken">A token that cancels the lookup.</param>
    /// <returns>
    /// The callback bound to the session when one exists and matches
    /// <typeparamref name="TCallback"/>; otherwise <see langword="null"/>.
    /// </returns>
    [Obsolete("Use IClientNotifications. Callback proxies are resolved internally at send time.")]
    ValueTask<TCallback?> GetCallbackAsync<TCallback>(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
        where TCallback : class;

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
