namespace Lakona.Game.Server.ReliablePush;

public sealed class ReliablePushOptions
{
    public int MaxPendingPerOwner { get; set; } = 256;

    public TimeSpan Retention { get; set; } = TimeSpan.FromMinutes(2);
}
