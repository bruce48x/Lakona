namespace Lakona.Game.Server.ReliablePush;

internal sealed class ReliablePushContinuityLostException : InvalidOperationException
{
    public ReliablePushContinuityLostException(bool newlyLost = false)
        : base("Reliable push pending capacity was exceeded.")
    {
        NewlyLost = newlyLost;
    }

    public bool NewlyLost { get; }
}
