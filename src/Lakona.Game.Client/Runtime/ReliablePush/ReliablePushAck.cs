using Lakona.Game.Abstractions;

namespace Lakona.Game.Client.ReliablePush
{
    public readonly struct ReliablePushAck
    {
        public ReliablePushAck(GameSessionKey session, ReliablePushSequence sequence)
        {
            Session = session;
            Sequence = sequence;
        }

        public GameSessionKey Session { get; }

        public ReliablePushSequence Sequence { get; }
    }
}
