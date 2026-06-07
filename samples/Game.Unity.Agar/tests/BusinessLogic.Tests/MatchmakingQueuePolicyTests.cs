using Agar.Sample.State.Contracts.Matchmaking;
using Agar.Sample.State.Matchmaking;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class MatchmakingQueuePolicyTests
{
    [Fact]
    public void FullRoomMatchesImmediately()
    {
        var now = DateTime.UtcNow;
        var tickets = CreateTickets(now, 10, secondsAgo: 1);

        var batchSize = MatchmakingQueuePolicy.GetMatchBatchSize(
            tickets,
            defaultRoomSize: 10,
            now,
            allowExpiredPartialBatch: false);

        Assert.Equal(10, batchSize);
    }

    [Fact]
    public void FrontQueueTicketMatchesAfterFiveSecondsEvenWhenRoomIsNotFull()
    {
        var now = DateTime.UtcNow;
        var tickets = CreateTickets(now, 1, secondsAgo: 5);

        var batchSize = MatchmakingQueuePolicy.GetMatchBatchSize(
            tickets,
            defaultRoomSize: 10,
            now,
            allowExpiredPartialBatch: true);

        Assert.Equal(1, batchSize);
    }

    [Fact]
    public void PartialBatchDoesNotMatchBeforeFiveSeconds()
    {
        var now = DateTime.UtcNow;
        var tickets = CreateTickets(now, 1, secondsAgo: 4);

        var batchSize = MatchmakingQueuePolicy.GetMatchBatchSize(
            tickets,
            defaultRoomSize: 10,
            now,
            allowExpiredPartialBatch: true);

        Assert.Equal(0, batchSize);
    }

    [Fact]
    public void ExpiredPartialBatchIncludesOnlyTicketsThatWaitedFiveSeconds()
    {
        var now = DateTime.UtcNow;
        var tickets = new[]
        {
            CreateTicket("old-1", now.AddSeconds(-6)),
            CreateTicket("old-2", now.AddSeconds(-5)),
            CreateTicket("new-1", now.AddSeconds(-2))
        };

        var batchSize = MatchmakingQueuePolicy.GetMatchBatchSize(
            tickets,
            defaultRoomSize: 10,
            now,
            allowExpiredPartialBatch: true);

        Assert.Equal(2, batchSize);
    }

    private static MatchmakingQueueTicket[] CreateTickets(DateTime now, int count, int secondsAgo)
    {
        return Enumerable.Range(0, count)
            .Select(index => CreateTicket($"ticket-{index}", now.AddSeconds(-secondsAgo)))
            .ToArray();
    }

    private static MatchmakingQueueTicket CreateTicket(string ticketId, DateTime enqueuedAtUtc)
    {
        return new MatchmakingQueueTicket
        {
            TicketId = ticketId,
            UserId = ticketId,
            SessionToken = $"session-{ticketId}",
            EnqueuedAtUtc = enqueuedAtUtc
        };
    }
}
