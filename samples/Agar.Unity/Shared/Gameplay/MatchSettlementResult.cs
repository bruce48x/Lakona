#nullable enable

using System.Collections.Generic;

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
}
