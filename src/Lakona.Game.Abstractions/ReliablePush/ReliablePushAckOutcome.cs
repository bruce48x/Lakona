namespace Lakona.Game.Abstractions
{
    public readonly struct ReliablePushAckOutcome
    {
        public ReliablePushAckOutcome(
            ReliablePushAckStatus status,
            GameSessionKey? session = null,
            long sequence = 0,
            string? reason = null)
        {
            Status = status;
            Session = session;
            Sequence = sequence;
            Reason = reason;
        }

        public ReliablePushAckStatus Status { get; }

        public GameSessionKey? Session { get; }

        public long Sequence { get; }

        public string? Reason { get; }

        public static ReliablePushAckOutcome Accepted()
        {
            return new ReliablePushAckOutcome(ReliablePushAckStatus.Accepted);
        }

        public static ReliablePushAckOutcome Accepted(GameSessionKey session, long sequence)
        {
            return new ReliablePushAckOutcome(ReliablePushAckStatus.Accepted, session, sequence);
        }

        public static ReliablePushAckOutcome Duplicate()
        {
            return new ReliablePushAckOutcome(ReliablePushAckStatus.Duplicate);
        }

        public static ReliablePushAckOutcome Duplicate(GameSessionKey session, long sequence)
        {
            return new ReliablePushAckOutcome(ReliablePushAckStatus.Duplicate, session, sequence);
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
