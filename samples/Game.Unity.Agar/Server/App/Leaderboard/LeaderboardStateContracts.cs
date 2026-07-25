
using System.Collections.Generic;
using MemoryPack;
using Shared.Interfaces;

namespace Server.App.Leaderboard
{
    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class LeaderboardSnapshot
    {
        [MemoryPackOrder(0)]
        public string PeriodStartUtc { get; set; } = "";

        [MemoryPackOrder(1)]
        public int SecondsUntilReset { get; set; }

        [MemoryPackOrder(2)]
        public List<LeaderboardEntry> Entries { get; set; } = new();

        [MemoryPackOrder(3)]
        public string PeriodStartLocalDate { get; set; } = "";
    }

}
