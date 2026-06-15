namespace Lakona.Game.Client.ReliablePush
{
    public readonly struct ReliablePushAck
    {
        public ReliablePushAck(string sessionId, Lakona.Game.Abstractions.ReliablePushSequence sequence)
        {
            SessionId = sessionId;
            Sequence = sequence;
        }

        public string SessionId { get; }

        public Lakona.Game.Abstractions.ReliablePushSequence Sequence { get; }
    }
}
