using Agar.Sample.State.Contracts.Leaderboard;
using Agar.Sample.State.Leaderboard;

namespace Server.Hotfix.State.Leaderboard;

public static class LeaderboardRankingPolicy
{
    public static List<LeaderboardEntrySnapshot> GetRankedEntries(IEnumerable<LeaderboardPlayerState> players)
    {
        return players
            .Where(static player => player.VictoryPoints > 0)
            .OrderByDescending(static player => player.VictoryPoints)
            .ThenByDescending(static player => player.WinCount)
            .ThenBy(static player => player.PlayerId, StringComparer.Ordinal)
            .Select((player, index) => new LeaderboardEntrySnapshot
            {
                PlayerId = player.PlayerId,
                VictoryPoints = player.VictoryPoints,
                WinCount = player.WinCount,
                Rank = index + 1
            })
            .ToList();
    }
}
