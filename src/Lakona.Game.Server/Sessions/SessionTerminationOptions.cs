namespace Lakona.Game.Server.Sessions;

/// <summary>
/// Configures how the server terminates a game session.
/// </summary>
public sealed class SessionTerminationOptions
{
    /// <summary>
    /// Gets or sets the maximum time to wait while sending a termination notice through
    /// the currently bound framework notification channel.
    /// </summary>
    /// <remarks>
    /// Set this to <see cref="TimeSpan.Zero"/> to skip client notification and
    /// proceed directly to connection close and session cleanup.
    /// </remarks>
    public TimeSpan NotifyTimeout { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets whether later resume attempts should see the terminal state.
    /// </summary>
    /// <remarks>
    /// When enabled, framework recovery can return
    /// <see cref="SessionResumeStatus.Terminated"/> with the retained notice until
    /// the Game Session resume deadline. When disabled, the Session and its
    /// recovery ticket are removed immediately and recovery reports state loss.
    /// </remarks>
    public bool KeepTerminalStateForResume { get; init; } = true;
}
