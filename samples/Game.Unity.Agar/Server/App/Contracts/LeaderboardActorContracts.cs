using MemoryPack;

namespace Server.App.State.Contracts.Leaderboard;

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class LeaderboardQueryRequest
{
    [MemoryPackOrder(0)]
    public int TopN { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class LeaderboardResetRequest
{
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class LeaderboardVictoryPointsRequest
{
    [MemoryPackOrder(0)]
    public string PlayerId { get; set; } = "";

    [MemoryPackOrder(1)]
    public int VictoryPoints { get; set; }

    [MemoryPackOrder(2)]
    public int WinCount { get; set; }
}
