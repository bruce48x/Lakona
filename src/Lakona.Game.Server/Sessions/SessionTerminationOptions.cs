namespace Lakona.Game.Server.Sessions;

/// <summary>
/// Configures how the server terminates a game session.
/// </summary>
public sealed class SessionTerminationOptions
{
    /// <summary>
    /// Gets or sets the maximum time to wait while notifying the currently bound client callback.
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
    /// When enabled, a later resume can return <see cref="SessionResumeStatus.Terminated"/>
    /// with the retained termination notice. When disabled, the same resume path
    /// reports state loss after termination.
    /// </remarks>
    public bool KeepTerminalStateForResume { get; init; } = true;
}
