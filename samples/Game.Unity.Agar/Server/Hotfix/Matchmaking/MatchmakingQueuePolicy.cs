using Server.App.Matchmaking;
using Server.Hotfix.Rooms;

namespace Server.Hotfix.Matchmaking;

public static class MatchmakingQueuePolicy
{
    public static TimeSpan MaxFrontQueueWait => TimeSpan.FromSeconds(5);

    public static int GetMatchBatchSize(
        IReadOnlyList<MatchmakingQueueTicket> pendingTickets,
        DateTime nowUtc,
        bool allowExpiredPartialBatch)
    {
        var pendingCount = pendingTickets.Count;
        if (pendingCount == 0)
        {
            return 0;
        }

        var roomSize = RoomRules.RoomSize;
        if (pendingCount >= roomSize)
        {
            return roomSize;
        }

        if (!allowExpiredPartialBatch || nowUtc - pendingTickets[0].EnqueuedAtUtc < MaxFrontQueueWait)
        {
            return 0;
        }

        return pendingCount;
    }
}
