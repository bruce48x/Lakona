using Server.App.Routing;
using Server.App.Leaderboard;
using Lakona.Game.Server.Actors;
using Shared.Interfaces;

namespace Server.App.Leaderboard;

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

    public List<LeaderboardEntry> Entries { get; set; } = new();

    public string PeriodStartLocalDate { get; set; } = "";
}

public sealed class LeaderboardActor : Actor<LeaderboardId>
{
    internal readonly TimeZoneInfo LeaderboardTimeZone = TimeZoneInfo.Local;
    internal LeaderboardState State = new();
}
