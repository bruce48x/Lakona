using Lakona.Game.Abstractions;
using Lakona.Game.Server.Sessions;

namespace Lakona.Game.Server.ReliablePush;

public static class ReliablePushAckDecider
{
    public static ReliablePushAckOutcome Decide(
        GameSessionKey currentSession,
        GameSessionKey acknowledgedSession,
        long sequence,
        long lastKnownSequence)
    {
        if (!currentSession.Equals(acknowledgedSession))
        {
            return ReliablePushAckOutcome.SessionMismatch("Acknowledgement belongs to a different session.");
        }

        if (sequence <= 0)
        {
            return ReliablePushAckOutcome.Duplicate(currentSession, sequence);
        }

        if (sequence > lastKnownSequence)
        {
            return ReliablePushAckOutcome.StateLost("Acknowledgement sequence is ahead of server state.");
        }

        return ReliablePushAckOutcome.Accepted(currentSession, sequence);
    }
}
