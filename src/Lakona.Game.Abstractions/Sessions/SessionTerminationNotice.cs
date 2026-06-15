using System;

namespace Lakona.Game.Abstractions
{
    public sealed class SessionTerminationNotice
    {
        public SessionTerminationNotice(
            SessionTerminationReason reason,
            string? message = null,
            DateTimeOffset? issuedAt = null)
        {
            Reason = reason;
            Message = message;
            IssuedAt = issuedAt ?? DateTimeOffset.UtcNow;
        }

        public SessionTerminationReason Reason { get; }

        public string? Message { get; }

        public DateTimeOffset IssuedAt { get; }
    }
}
