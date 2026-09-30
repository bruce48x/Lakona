namespace Lakona.Game.Server.ReliablePush;

internal sealed class ReliablePushContinuityLostException : InvalidOperationException
{
    public ReliablePushContinuityLostException(bool newlyLost = false, int pendingCount = 0, long lastSequence = 0, long acknowledgedSequence = 0)
        : base("Reliable push pending capacity was exceeded.")
    {
        NewlyLost = newlyLost;
        PendingCount = pendingCount;
        LastSequence = lastSequence;
        AcknowledgedSequence = acknowledgedSequence;
    }

    public bool NewlyLost { get; }

    public int PendingCount { get; }

    public long LastSequence { get; }

    public long AcknowledgedSequence { get; }
}
