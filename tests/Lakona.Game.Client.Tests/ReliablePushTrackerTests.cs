using Lakona.Game.Client.ReliablePush;
using Xunit;

namespace Lakona.Game.Client.Tests;

public sealed class ReliablePushTrackerTests
{
    [Fact]
    public void NewPositiveSequenceShouldBeAppliedAndAcknowledged()
    {
        var tracker = new ReliablePushTracker();

        var decision = tracker.Decide(1);

        Assert.Equal(1, decision.Sequence);
        Assert.True(decision.ShouldApply);
        Assert.True(decision.ShouldAck);
        Assert.False(decision.IsDuplicate);
    }

    [Fact]
    public void DuplicateSequenceShouldOnlyBeAcknowledged()
    {
        var tracker = new ReliablePushTracker();
        tracker.MarkApplied(10);

        var decision = tracker.Decide(10);

        Assert.False(decision.ShouldApply);
        Assert.True(decision.ShouldAck);
        Assert.True(decision.IsDuplicate);
    }

    [Fact]
    public void NonPositiveSequenceShouldApplyWithoutAcknowledgement()
    {
        var tracker = new ReliablePushTracker();

        var decision = tracker.Decide(0);

        Assert.True(decision.ShouldApply);
        Assert.False(decision.ShouldAck);
        Assert.False(decision.IsDuplicate);
    }

    [Fact]
    public void MarkAppliedOnlyMovesSequenceForward()
    {
        var tracker = new ReliablePushTracker();

        tracker.MarkApplied(8);
        tracker.MarkApplied(3);

        Assert.Equal(8, tracker.LastAppliedSequence);
    }

    [Fact]
    public void ResetClearsAppliedSequence()
    {
        var tracker = new ReliablePushTracker();
        tracker.MarkApplied(8);

        tracker.Reset();

        Assert.Equal(0, tracker.LastAppliedSequence);
    }
}
