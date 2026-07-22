using Server.App.State.Leaderboard;
using Shared.Interfaces;

namespace Server.Hotfix.State.Leaderboard;

public static class LeaderboardRankingPolicy
{
    public static List<LeaderboardEntry> GetRankedEntries(IEnumerable<LeaderboardPlayerState> players)
    {
        return players
            .Where(static player => player.VictoryPoints > 0)
            .OrderByDescending(static player => player.VictoryPoints)
            .ThenByDescending(static player => player.WinCount)
            .ThenBy(static player => player.PlayerId, StringComparer.Ordinal)
            .Select((player, index) => new LeaderboardEntry
            {
                PlayerId = player.PlayerId,
                VictoryPoints = player.VictoryPoints,
                WinCount = player.WinCount,
                Rank = index + 1
            })
            .ToList();
    }
}
