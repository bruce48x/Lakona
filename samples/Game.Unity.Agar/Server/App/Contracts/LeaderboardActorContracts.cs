using Agar.Sample.State.Leaderboard;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Agar.Sample.State.Contracts.Leaderboard;

[HotfixActorContract(typeof(LeaderboardActor))]
public interface ILeaderboardActorContract
{
    ValueTask<LeaderboardSnapshot> GetLeaderboardAsync(
        LeaderboardQueryRequest request,
        CancellationToken cancellationToken = default);

    ValueTask ResetWeeklyIfNeededAsync(
        LeaderboardResetRequest request,
        CancellationToken cancellationToken = default);

    ValueTask RecordVictoryPointsAsync(
        LeaderboardVictoryPointsRequest request,
        CancellationToken cancellationToken = default);
}

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
