using Lakona.Game.Abstractions;
using Lakona.Game.Client.ReliablePush;
using Xunit;

namespace Lakona.Game.Client.Tests;

public sealed class ReliablePushInboxTests
{
    [Fact]
    public async Task ProcessAppliesNewSequenceThenAcknowledges()
    {
        var inbox = new ReliablePushInbox();
        var session = new GameSessionKey("player-a", "session-a", 1);
        var applied = new List<string>();
        var acknowledged = new List<ReliablePushAck>();
        inbox.StartSession(session);

        var result = await inbox.ProcessAsync(
            ReliablePushSequence.From(1),
            "matched",
            (payload, _) =>
            {
                applied.Add(payload);
                return ValueTask.CompletedTask;
            },
            (ack, _) =>
            {
                acknowledged.Add(ack);
                return ValueTask.FromResult(ReliablePushAckOutcome.Accepted());
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.Decision.ShouldApply);
        Assert.Equal("matched", Assert.Single(applied));
        Assert.Equal(1, Assert.Single(acknowledged).Sequence.Value);
        Assert.Equal(1, inbox.LastAppliedSequence);
    }

    [Fact]
    public async Task DuplicateSequenceOnlyAcknowledges()
    {
        var inbox = new ReliablePushInbox();
        var session = new GameSessionKey("player-a", "session-a", 1);
        var applyCount = 0;
        var ackCount = 0;
        inbox.StartSession(session, lastAppliedSequence: 5);

        var result = await inbox.ProcessAsync(
            ReliablePushSequence.From(5),
            "duplicate",
            (_, _) =>
            {
                applyCount++;
                return ValueTask.CompletedTask;
            },
            (_, _) =>
            {
                ackCount++;
                return ValueTask.FromResult(ReliablePushAckOutcome.Duplicate());
            },
            TestContext.Current.CancellationToken);

        Assert.False(result.Decision.ShouldApply);
        Assert.True(result.Decision.IsDuplicate);
        Assert.Equal(0, applyCount);
        Assert.Equal(1, ackCount);
        Assert.Equal(5, inbox.LastAppliedSequence);
    }

    [Fact]
    public async Task NewSessionUsesIsolatedCursor()
    {
        var store = new InMemoryReliablePushCursorStore();
        var first = new GameSessionKey("player-a", "session-a", 1);
        var second = new GameSessionKey("player-a", "session-b", 2);
        await store.SaveAsync(first, 10, TestContext.Current.CancellationToken);
        var inbox = new ReliablePushInbox(store);

        await inbox.StartSessionAsync(second, TestContext.Current.CancellationToken);

        Assert.Equal(0, inbox.LastAppliedSequence);
    }

    [Fact]
    public void NonPositiveSequenceCannotBeCreated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReliablePushSequence.From(0));
    }
}
