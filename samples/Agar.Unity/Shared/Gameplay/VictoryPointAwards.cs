#nullable enable

using System;

namespace Shared.Gameplay
{
    public static class VictoryPointAwards
    {
        public const string BotPrefix = "AI";

        public static int GetPointsForRank(int rank)
        {
            return rank switch
            {
                1 => 10,
                2 => 7,
                3 => 5,
                4 => 3,
                5 => 1,
                _ => 0
            };
        }

        public static bool IsBotPlayer(string playerId)
        {
            return playerId.StartsWith(BotPrefix, StringComparison.Ordinal);
        }
    }
}
