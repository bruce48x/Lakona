using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Leaderboard;
using Lakona.Game.Server.Actors;

namespace Agar.Sample.State.Leaderboard;

public sealed class LeaderboardState
{
    public string CurrentPeriodStartUtc { get; set; } = "";

    public Dictionary<string, LeaderboardPlayerState> Players { get; set; } = new(StringComparer.Ordinal);

    public List<WeeklyLeaderboardSnapshot> WeeklySnapshots { get; set; } = new();

    public string CurrentPeriodStartLocalDate { get; set; } = "";
}

public sealed class LeaderboardPlayerState
{
    public string PlayerId { get; set; } = "";

    public int VictoryPoints { get; set; }

    public int WinCount { get; set; }
}

public sealed class WeeklyLeaderboardSnapshot
{
    public string PeriodStartUtc { get; set; } = "";

    public List<LeaderboardEntrySnapshot> Entries { get; set; } = new();

    public string PeriodStartLocalDate { get; set; } = "";
}

public sealed class LeaderboardActor : Actor<LeaderboardId>
{
    internal readonly TimeZoneInfo LeaderboardTimeZone = TimeZoneInfo.Local;
    internal LeaderboardState State = new();
}
