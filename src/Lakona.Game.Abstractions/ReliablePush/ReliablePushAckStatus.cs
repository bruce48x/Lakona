namespace Lakona.Game.Abstractions
{
    public enum ReliablePushAckStatus
    {
        Accepted,
        Duplicate,
        StateRefreshRequired,
        StateLost,
        SessionMismatch
    }
}
