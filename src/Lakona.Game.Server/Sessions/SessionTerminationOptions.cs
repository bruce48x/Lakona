namespace Lakona.Game.Server.Sessions;

public sealed class SessionTerminationOptions
{
    public TimeSpan NotifyTimeout { get; init; } = TimeSpan.FromSeconds(1);

    public bool KeepTerminalStateForResume { get; init; } = true;
}
