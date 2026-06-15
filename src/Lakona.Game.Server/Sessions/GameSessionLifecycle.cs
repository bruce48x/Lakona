using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

/// <summary>
/// Describes a newly opened game RPC connection before it is bound to a game session endpoint.
/// </summary>
public sealed class GameConnectionContext
{
    /// <summary>
    /// Initializes a new connection lifecycle context.
    /// </summary>
    /// <param name="connectionId">The framework-assigned RPC connection identifier.</param>
    /// <param name="displayName">A human-readable connection label for diagnostics.</param>
    public GameConnectionContext(string connectionId, string displayName)
    {
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
    }

    /// <summary>
    /// Gets the framework-assigned RPC connection identifier.
    /// </summary>
    public string ConnectionId { get; }

    /// <summary>
    /// Gets a human-readable connection label for logs and diagnostics.
    /// </summary>
    public string DisplayName { get; }
}

/// <summary>
/// Describes a game session that is associated with an RPC connection.
/// </summary>
public sealed class GameSessionBindingContext
{
    /// <summary>
    /// Initializes a new endpoint binding lifecycle context.
    /// </summary>
    /// <param name="endpoint">The session endpoint affected by the lifecycle event.</param>
    /// <param name="connectionId">The RPC connection currently associated with the endpoint.</param>
    /// <param name="callbackContractTypes">The callback contracts exposed by the bound client endpoint.</param>
    public GameSessionBindingContext(
        GameSessionKey session,
        string connectionId,
        IReadOnlyList<Type> callbackContractTypes)
    {
        Session = session;
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        CallbackContractTypes = callbackContractTypes ?? throw new ArgumentNullException(nameof(callbackContractTypes));
    }

    /// <summary>
    /// Gets the session affected by the lifecycle event.
    /// </summary>
    public GameSessionKey Session { get; }

    /// <summary>
    /// Gets the RPC connection currently associated with the endpoint.
    /// </summary>
    public string ConnectionId { get; }

    /// <summary>
    /// Gets the callback contracts exposed by the bound client endpoint.
    /// </summary>
    public IReadOnlyList<Type> CallbackContractTypes { get; }
}

/// <summary>
/// Describes a game session termination event.
/// </summary>
public sealed class GameSessionTerminationContext
{
    /// <summary>
    /// Initializes a new session termination lifecycle context.
    /// </summary>
    /// <param name="session">The session affected by the termination.</param>
    /// <param name="notice">The termination notice emitted for the session.</param>
    public GameSessionTerminationContext(GameSessionKey session, SessionTerminationNotice notice)
    {
        Session = session;
        Notice = notice ?? throw new ArgumentNullException(nameof(notice));
    }

    /// <summary>
    /// Gets the session affected by the termination.
    /// </summary>
    public GameSessionKey Session { get; }

    /// <summary>
    /// Gets the termination notice emitted for the session.
    /// </summary>
    public SessionTerminationNotice Notice { get; }
}

/// <summary>
/// Receives framework-owned connection and session lifecycle notifications.
/// </summary>
/// <remarks>
/// Register implementations in dependency injection to observe lifecycle events for presence,
/// matchmaking cleanup, room membership cleanup, metrics, or other game-owned side effects.
/// Handlers should be idempotent because reconnects, disconnect cleanup, and expiration can
/// report related endpoint state at different points in time.
/// </remarks>
public interface IGameSessionLifecycleHandler
{
    /// <summary>
    /// Called after a game RPC connection is opened and before it is bound to a game session.
    /// </summary>
    /// <param name="context">Information about the opened connection.</param>
    /// <param name="cancellationToken">A token that is canceled when the connection startup notification should stop.</param>
    ValueTask OnConnectionOpenedAsync(
        GameConnectionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after a connection is bound to a game session and the session becomes active.
    /// </summary>
    /// <param name="context">Information about the endpoint, connection, and callback contracts.</param>
    /// <param name="cancellationToken">A token that is canceled when the endpoint binding notification should stop.</param>
    ValueTask OnSessionBoundAsync(
        GameSessionBindingContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when a bound session is marked disconnected but remains eligible for later cleanup or resume.
    /// </summary>
    /// <param name="context">Information about the disconnected endpoint and its last known connection.</param>
    /// <param name="cancellationToken">A token that is canceled when the disconnect notification should stop.</param>
    ValueTask OnSessionDisconnectedAsync(
        GameSessionBindingContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when a previously disconnected session expires and should no longer be treated as resumable.
    /// </summary>
    /// <param name="context">Information about the expired endpoint and its last known connection.</param>
    /// <param name="cancellationToken">A token that is canceled when the endpoint expiration notification should stop.</param>
    ValueTask OnSessionExpiredAsync(
        GameSessionBindingContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when a game session is terminated by the framework or game code.
    /// </summary>
    /// <param name="context">Information about the terminated session and termination reason.</param>
    /// <param name="cancellationToken">A token that is canceled when the termination notification should stop.</param>
    ValueTask OnSessionTerminatedAsync(
        GameSessionTerminationContext context,
        CancellationToken cancellationToken = default);
}
