
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Server.App.State.Contracts.Leaderboard
{
    public sealed class LeaderboardSnapshot
    {
        public string PeriodStartUtc { get; set; } = "";

        public int SecondsUntilReset { get; set; }

        public List<LeaderboardEntrySnapshot> Entries { get; set; } = new();

        public string PeriodStartLocalDate { get; set; } = "";
    }

    public sealed class LeaderboardEntrySnapshot
    {
        public string PlayerId { get; set; } = "";

        public int VictoryPoints { get; set; }

        public int WinCount { get; set; }

        public int Rank { get; set; }
    }
}
