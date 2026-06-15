namespace Lakona.Game.Server.Sessions;

public sealed class SessionCleanupOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan DisconnectedSessionRetention { get; set; } = TimeSpan.FromMinutes(2);
}
