#nullable enable

using System.Collections.Generic;
using System;
using System.Linq;
using Shared.Interfaces;

namespace Shared.Gameplay
{
    public sealed class MatchSettlementResult
    {
        public string WinnerPlayerId { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public List<MatchSettlementEntry> Entries { get; } = new();
    }

    public sealed class MatchSettlementEntry
    {
        public string PlayerId { get; set; } = string.Empty;
        public int Rank { get; set; }
        public int Mass { get; set; }
        public bool IsWinner { get; set; }
        public bool IsBot { get; set; }
        public int VictoryPoints { get; set; }
    }

    public static class MatchSettlementRules
    {
        public static MatchSettlementResult Settle(WorldState worldState)
        {
            if (worldState == null)
            {
                throw new ArgumentNullException(nameof(worldState));
            }

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
}
