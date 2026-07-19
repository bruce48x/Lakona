using System;

namespace Lakona.Game.Abstractions
{
    public readonly struct ReliablePushMetadata
    {
        public ReliablePushMetadata(
            string sessionId,
            ReliablePushSequence sequence,
            string kind)
        {
            SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
            Sequence = sequence;
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
        }

        public string SessionId { get; }

        public ReliablePushSequence Sequence { get; }

        public string Kind { get; }
    }
}
