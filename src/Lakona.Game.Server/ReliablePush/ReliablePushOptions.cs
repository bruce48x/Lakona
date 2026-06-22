namespace Lakona.Game.Server.ReliablePush;

public sealed class ReliablePushOptions
{
    public bool Enabled { get; set; } = true;

    public int MaxPendingPerOwner { get; set; } = 256;

    public TimeSpan Retention { get; set; } = TimeSpan.FromMinutes(2);
}
