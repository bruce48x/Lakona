using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

/// <summary>
/// Describes the result of a game session resume attempt.
/// </summary>
/// <remarks>
/// Resume decisions separate transport reconnection from authoritative game
/// state. A session can be rebound, require a state refresh, be missing, be
/// unauthorized, or report a retained terminal state.
/// </remarks>
public sealed class SessionResumeDecision
{
    /// <summary>
    /// Initializes a new resume decision.
    /// </summary>
    /// <param name="status">The resume outcome.</param>
    /// <param name="session">The session associated with the outcome, when one is available.</param>
    /// <param name="reason">Optional diagnostic reason for non-resumed or refresh-required outcomes.</param>
    /// <param name="termination">Optional retained termination notice for terminated outcomes.</param>
    public SessionResumeDecision(
        SessionResumeStatus status,
        GameSessionKey? session,
        string? reason = null,
        SessionTerminationNotice? termination = null)
    {
        Status = status;
        Session = session;
        Reason = reason;
        Termination = termination;
    }

    /// <summary>
    /// Gets the resume outcome.
    /// </summary>
    public SessionResumeStatus Status { get; }

    /// <summary>
    /// Gets the session associated with the outcome, when one is available.
    /// </summary>
    public GameSessionKey? Session { get; }

    /// <summary>
    /// Gets optional diagnostic text explaining the outcome.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// Gets the retained termination notice when <see cref="Status"/> is
    /// <see cref="SessionResumeStatus.Terminated"/>.
    /// </summary>
    public SessionTerminationNotice? Termination { get; }

    /// <summary>
    /// Creates a decision indicating that the session was resumed and can be rebound.
    /// </summary>
    /// <param name="session">The resumed session.</param>
    /// <returns>A resumed decision for <paramref name="session"/>.</returns>
    public static SessionResumeDecision Resumed(GameSessionKey session)
    {
        return new SessionResumeDecision(SessionResumeStatus.Resumed, session);
    }

    /// <summary>
    /// Creates a decision indicating that the requested session state is no longer available.
    /// </summary>
    /// <param name="reason">Optional diagnostic reason for the lost state.</param>
    /// <returns>A state-lost decision.</returns>
    public static SessionResumeDecision StateLost(string? reason = null)
    {
        return new SessionResumeDecision(SessionResumeStatus.StateLost, null, reason);
    }

    /// <summary>
    /// Creates a decision indicating that the session was found but the client must refresh game state.
    /// </summary>
    /// <param name="session">The session that can be rebound after refresh handling.</param>
    /// <param name="reason">Optional diagnostic reason for the required refresh.</param>
    /// <returns>A state-refresh-required decision.</returns>
    public static SessionResumeDecision StateRefreshRequired(
        GameSessionKey session,
        string? reason = null)
    {
        return new SessionResumeDecision(SessionResumeStatus.StateRefreshRequired, session, reason);
    }

    /// <summary>
    /// Creates a decision indicating that the resume request is not authorized.
    /// </summary>
    /// <param name="reason">Optional diagnostic reason for the authorization failure.</param>
    /// <returns>An unauthorized decision.</returns>
    public static SessionResumeDecision Unauthorized(string? reason = null)
    {
        return new SessionResumeDecision(SessionResumeStatus.Unauthorized, null, reason);
    }

    /// <summary>
    /// Creates a decision indicating that the session had already been terminated.
    /// </summary>
    /// <param name="session">The terminated session.</param>
    /// <param name="notice">The retained termination notice.</param>
    /// <returns>A terminated decision for <paramref name="session"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="notice"/> is <see langword="null"/>.</exception>
    public static SessionResumeDecision Terminated(
        GameSessionKey session,
        SessionTerminationNotice notice)
    {
        if (notice is null)
        {
            throw new ArgumentNullException(nameof(notice));
        }

        return new SessionResumeDecision(
            SessionResumeStatus.Terminated,
            session,
            notice.Message,
            notice);
    }
}
