using Shared.Gameplay;
using Shared.Interfaces;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Agar.Sample.Hotfix.Gameplay;

[FriendOf(typeof(ArenaSimulation))]
[HotfixBehaviorOf(typeof(ArenaSimulation))]
public static class ArenaSettlementBehavior
{
    public static MatchSettlementResult SettleMatch(this ArenaSimulation self, WorldState worldState)
    {
        var rankedPlayers = worldState.Players
            .OrderByDescending(static player => player.Mass)
            .ThenBy(static player => player.PlayerId, StringComparer.Ordinal)
            .ToArray();

        var winner = rankedPlayers.FirstOrDefault()?.PlayerId ?? string.Empty;
        var result = new MatchSettlementResult
        {
            WinnerPlayerId = winner,
            Reason = "Round timer elapsed."
        };

        for (var index = 0; index < rankedPlayers.Length; index++)
        {
            var player = rankedPlayers[index];
            var rank = index + 1;
            var isBot = VictoryPointAwards.IsBotPlayer(player.PlayerId);
            result.Entries.Add(new MatchSettlementEntry
            {
                PlayerId = player.PlayerId,
                Rank = rank,
                Mass = NormalizeRankingMass(player.Mass),
                IsWinner = string.Equals(player.PlayerId, winner, StringComparison.Ordinal),
                IsBot = isBot,
                VictoryPoints = isBot ? 0 : VictoryPointAwards.GetPointsForRank(rank)
            });
        }

        return result;
    }

    private static int NormalizeRankingMass(float mass)
    {
        return float.IsNaN(mass) || float.IsInfinity(mass)
            ? 0
            : Math.Max(0, (int)MathF.Round(mass, MidpointRounding.AwayFromZero));
    }
}
