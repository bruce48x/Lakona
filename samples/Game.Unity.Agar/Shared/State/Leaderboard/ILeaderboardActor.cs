
using System.Collections.Generic;
using System.Threading.Tasks;
using MemoryPack;

namespace Server.App.State.Contracts.Leaderboard
{
    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class LeaderboardSnapshot
    {
        [MemoryPackOrder(0)]
        public string PeriodStartUtc { get; set; } = "";

        [MemoryPackOrder(1)]
        public int SecondsUntilReset { get; set; }

        [MemoryPackOrder(2)]
        public List<LeaderboardEntrySnapshot> Entries { get; set; } = new();

        [MemoryPackOrder(3)]
        public string PeriodStartLocalDate { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class LeaderboardEntrySnapshot
    {
        [MemoryPackOrder(0)]
        public string PlayerId { get; set; } = "";

        [MemoryPackOrder(1)]
        public int VictoryPoints { get; set; }

        [MemoryPackOrder(2)]
        public int WinCount { get; set; }

        [MemoryPackOrder(3)]
        public int Rank { get; set; }
    }
}
