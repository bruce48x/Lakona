namespace Lakona.Game.Server.Sessions;

/// <summary>
/// Identifies the outcome of a game session resume attempt.
/// </summary>
public enum SessionResumeStatus
{
    /// <summary>
    /// The session was found and can continue on the new connection.
    /// </summary>
    Resumed,

    /// <summary>
    /// The session was found, but the client must refresh authoritative game state before continuing.
    /// </summary>
    StateRefreshRequired,

    /// <summary>
    /// The requested session no longer exists or is no longer resumable.
    /// </summary>
    StateLost,

    /// <summary>
    /// The resume token or request was rejected by session validation.
    /// </summary>
    Unauthorized,

    /// <summary>
    /// The session was already terminated and retained its terminal state for resume reporting.
    /// </summary>
    Terminated
}
