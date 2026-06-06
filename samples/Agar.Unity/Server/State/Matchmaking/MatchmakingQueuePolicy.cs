using Agar.Sample.State.Contracts.Matchmaking;

namespace Agar.Sample.State.Matchmaking;

public static class MatchmakingQueuePolicy
{
    public static readonly TimeSpan MaxFrontQueueWait = TimeSpan.FromSeconds(5);

    public static int GetMatchBatchSize(
        IReadOnlyList<MatchmakingQueueTicket> pendingTickets,
        int defaultRoomSize,
        DateTime nowUtc,
        bool allowExpiredPartialBatch)
    {
        if (pendingTickets.Count == 0)
        {
            return 0;
        }

        var roomSize = NormalizeRoomSize(defaultRoomSize);
        if (pendingTickets.Count >= roomSize)
        {
            return roomSize;
        }

        if (!allowExpiredPartialBatch || nowUtc - pendingTickets[0].EnqueuedAtUtc < MaxFrontQueueWait)
        {
            return 0;
        }

        return pendingTickets
            .TakeWhile(ticket => nowUtc - ticket.EnqueuedAtUtc >= MaxFrontQueueWait)
            .Count();
    }

    public static int NormalizeRoomSize(int defaultRoomSize)
    {
        return Math.Clamp(defaultRoomSize <= 0 ? 10 : defaultRoomSize, 1, 10);
    }
}
