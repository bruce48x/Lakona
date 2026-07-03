namespace Agar.Sample.State.Contracts.Leaderboard;

public sealed class LeaderboardQueryRequest
{
    public int TopN { get; set; }
}

public sealed class LeaderboardResetRequest
{
}

public sealed class LeaderboardVictoryPointsRequest
{
    public string PlayerId { get; set; } = "";

    public int VictoryPoints { get; set; }

    public int WinCount { get; set; }
}
