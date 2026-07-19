using System;

namespace Lakona.Game.Abstractions
{
    public readonly struct ReliablePushAckRequest
    {
        public ReliablePushAckRequest(string sessionId, ReliablePushSequence sequence)
        {
            SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
            Sequence = sequence;
        }

        public string SessionId { get; }

        public ReliablePushSequence Sequence { get; }
    }
}
