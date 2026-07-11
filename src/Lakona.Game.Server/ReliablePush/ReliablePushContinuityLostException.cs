namespace Lakona.Game.Server.ReliablePush;

internal sealed class ReliablePushContinuityLostException : InvalidOperationException
{
    public ReliablePushContinuityLostException()
        : base("Reliable push pending capacity was exceeded.")
    {
    }
}
