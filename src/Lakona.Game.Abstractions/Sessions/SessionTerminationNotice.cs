using System;

namespace Lakona.Game.Abstractions
{
    public sealed class SessionTerminationNotice
    {
        public SessionTerminationNotice(
            GameSessionKey session,
            SessionTerminationReason reason,
            string? message = null,
            DateTimeOffset? issuedAt = null)
        {
            Session = session;
            Reason = reason;
            Message = message;
            IssuedAt = issuedAt ?? DateTimeOffset.UtcNow;
        }

        public GameSessionKey Session { get; }

        public SessionTerminationReason Reason { get; }

        public string? Message { get; }

        public DateTimeOffset IssuedAt { get; }
    }
}
