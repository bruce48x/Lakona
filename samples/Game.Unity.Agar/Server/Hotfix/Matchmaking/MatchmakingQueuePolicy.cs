using Server.App.Matchmaking;
using Server.Hotfix.Rooms;

namespace Server.Hotfix.Matchmaking;

public static class MatchmakingQueuePolicy
{
    public static TimeSpan MaxFrontQueueWait => TimeSpan.FromSeconds(5);

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

        var roomSize = RoomRules.RoomSize;
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
}
