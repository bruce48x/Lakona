namespace Lakona.Game.Abstractions
{
    public readonly struct ReliablePushAckOutcome
    {
        public ReliablePushAckOutcome(
            ReliablePushAckStatus status,
            long sequence = 0,
            string? reason = null)
        {
            Status = status;
            Sequence = sequence;
            Reason = reason;
        }

        public ReliablePushAckStatus Status { get; }

        public long Sequence { get; }

        public string? Reason { get; }

        public static ReliablePushAckOutcome Accepted()
        {
            return new ReliablePushAckOutcome(ReliablePushAckStatus.Accepted);
        }

        public static ReliablePushAckOutcome Duplicate()
        {
            return new ReliablePushAckOutcome(ReliablePushAckStatus.Duplicate);
        }

        public static ReliablePushAckOutcome StateRefreshRequired(string? reason = null)
        {
            return new ReliablePushAckOutcome(ReliablePushAckStatus.StateRefreshRequired, reason: reason);
        }

        public static ReliablePushAckOutcome StateLost(string? reason = null)
        {
            return new ReliablePushAckOutcome(ReliablePushAckStatus.StateLost, reason: reason);
        }

        public static ReliablePushAckOutcome SessionMismatch(string? reason = null)
        {
            return new ReliablePushAckOutcome(ReliablePushAckStatus.SessionMismatch, reason: reason);
        }
    }
}
