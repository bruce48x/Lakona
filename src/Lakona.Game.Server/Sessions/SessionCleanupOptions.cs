namespace Lakona.Game.Server.Sessions;

public sealed class SessionCleanupOptions
{
    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan ResumeWindow { get; set; } = TimeSpan.FromSeconds(60);
}
