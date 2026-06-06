using Lakona.Game.Abstractions;
using Lakona.Game.Server.Sessions;

namespace Lakona.Game.Server.ReliablePush;

public sealed class ReliablePushAckService : IReliablePushAckService
{
    private readonly IReliablePushOutbox _outbox;

    public ReliablePushAckService(IReliablePushOutbox outbox)
    {
        _outbox = outbox;
    }

    public async ValueTask<ReliablePushAckOutcome> AckAsync(
        GameSessionKey currentSession,
        GameSessionKey acknowledgedSession,
        long sequence,
        CancellationToken cancellationToken = default)
    {
        var lastKnownSequence = _outbox.GetLastSequence(currentSession);
        var outcome = ReliablePushAckDecider.Decide(
            currentSession,
            acknowledgedSession,
            sequence,
            lastKnownSequence);

        if (outcome.Status == ReliablePushAckStatus.Accepted)
        {
            await _outbox.AckAsync(currentSession, sequence, cancellationToken).ConfigureAwait(false);
        }

        return outcome;
    }
}
