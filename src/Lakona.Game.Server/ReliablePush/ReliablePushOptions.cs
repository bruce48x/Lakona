namespace Lakona.Game.Server.ReliablePush;

public sealed class ReliablePushOptions
{
    public int MaxPendingPerSession { get; set; } = 256;

    public int MaxPendingBytesPerSession { get; set; } = 128 * 1024 * 1024;

    public int MaxPendingBytesPerProcess { get; set; } = 512 * 1024 * 1024;
}
