using Lakona.Game.Abstractions;
using Lakona.Game.Server.ReliablePush;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class ReliablePushAckDeciderTests
{
    [Fact]
    public void MatchingSessionAndKnownSequenceIsAccepted()
    {
        var session = new GameSessionKey("player-a", "session-a", 1);

        var outcome = ReliablePushAckDecider.Decide(session, session, sequence: 3, lastKnownSequence: 5);

        Assert.Equal(ReliablePushAckStatus.Accepted, outcome.Status);
    }

    [Fact]
    public void DifferentGenerationIsSessionMismatch()
    {
        var current = new GameSessionKey("player-a", "session-b", 2);
        var old = new GameSessionKey("player-a", "session-a", 1);

        var outcome = ReliablePushAckDecider.Decide(current, old, sequence: 3, lastKnownSequence: 5);

        Assert.Equal(ReliablePushAckStatus.SessionMismatch, outcome.Status);
    }

    [Fact]
    public void SequenceAheadOfServerStateIsStateLost()
    {
        var session = new GameSessionKey("player-a", "session-a", 1);

        var outcome = ReliablePushAckDecider.Decide(session, session, sequence: 6, lastKnownSequence: 5);

        Assert.Equal(ReliablePushAckStatus.StateLost, outcome.Status);
    }
}
