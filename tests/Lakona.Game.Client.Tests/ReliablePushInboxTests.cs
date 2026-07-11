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
        var session = "session-a";
        var applied = new List<string>();
        var acknowledged = new List<ReliablePushAckRequest>();
        inbox.StartSession(session, sessionGeneration: 7);

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
        var ack = Assert.Single(acknowledged);
        Assert.Equal(session, ack.SessionId);
        Assert.Equal(7, ack.SessionGeneration);
        Assert.Equal(1, ack.Sequence.Value);
        Assert.Equal(1, inbox.LastAppliedSequence);
    }

    [Fact]
    public async Task DuplicateSequenceOnlyAcknowledges()
    {
        var inbox = new ReliablePushInbox();
        var session = "session-a";
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
    public async Task Gap_sequence_is_rejected_before_business_application_or_acknowledgement()
    {
        var inbox = new ReliablePushInbox();
        var applyCount = 0;
        var ackCount = 0;
        inbox.StartSession("session-a", sessionGeneration: 1, lastAppliedSequence: 2);

        var result = await inbox.ProcessAsync(
            ReliablePushSequence.From(4),
            "gap",
            (_, _) =>
            {
                applyCount++;
                return default;
            },
            (_, _) =>
            {
                ackCount++;
                return new ValueTask<ReliablePushAckOutcome>(ReliablePushAckOutcome.Accepted());
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.Decision.IsGap);
        Assert.Equal(0, applyCount);
        Assert.Equal(0, ackCount);
        Assert.Equal(2, inbox.LastAppliedSequence);
    }

    [Fact]
    public async Task NewSessionUsesIsolatedCursor()
    {
        var store = new InMemoryReliablePushCursorStore();
        var first = "session-a";
        var second = "session-b";
        await store.SaveAsync(first, 1, 10, TestContext.Current.CancellationToken);
        var inbox = new ReliablePushInbox(store);

        await inbox.StartSessionAsync(second, TestContext.Current.CancellationToken);

        Assert.Equal(0, inbox.LastAppliedSequence);
    }

    [Fact]
    public async Task NewSessionGenerationUsesIsolatedCursor()
    {
        var store = new InMemoryReliablePushCursorStore();
        var session = "session-a";
        await store.SaveAsync(session, 1, 10, TestContext.Current.CancellationToken);
        var inbox = new ReliablePushInbox(store);

        await inbox.StartSessionAsync(session, 2, TestContext.Current.CancellationToken);

        Assert.Equal(0, inbox.LastAppliedSequence);
        Assert.Equal(2, inbox.CurrentSessionGeneration);
    }

    [Fact]
    public async Task MetadataForDifferentSessionGenerationIsRejectedBeforeApply()
    {
        var inbox = new ReliablePushInbox();
        var applyCount = 0;
        var ackCount = 0;
        inbox.StartSession("session-a", sessionGeneration: 2);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await inbox.ProcessAsync(
                new ReliablePushMetadata(
                    "session-a",
                    1,
                    ReliablePushSequence.From(1),
                    "test.notification"),
                _ =>
                {
                    applyCount++;
                    return default;
                },
                (_, _) =>
                {
                    ackCount++;
                    return new ValueTask<ReliablePushAckOutcome>(ReliablePushAckOutcome.SessionMismatch());
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(0, applyCount);
        Assert.Equal(0, ackCount);
        Assert.Equal(0, inbox.LastAppliedSequence);
    }

    [Fact]
    public void NonPositiveSequenceCannotBeCreated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReliablePushSequence.From(0));
    }
}
