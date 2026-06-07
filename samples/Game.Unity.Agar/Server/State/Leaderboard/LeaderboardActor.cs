using Agar.Sample.State.Contracts.Leaderboard;
using Agar.Sample.State.Users;
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

public sealed class LeaderboardActor : Actor
{
    private readonly TimeZoneInfo _leaderboardTimeZone = TimeZoneInfo.Local;
    private LeaderboardState _state = new();

    protected override ValueTask OnActivateAsync(CancellationToken cancellationToken)
    {
        EnsurePeriodInitialized(DateTime.UtcNow);
        return ValueTask.CompletedTask;
    }

    public async Task<LeaderboardSnapshot> GetLeaderboardAsync(int topN)
    {
        await ResetWeeklyIfNeededAsync().ConfigureAwait(false);

        topN = Math.Clamp(topN, 1, 100);
        var now = DateTime.UtcNow;
        var entries = GetRankedEntries()
            .Take(topN)
            .ToList();

        return new LeaderboardSnapshot
        {
            PeriodStartLocalDate = _state.CurrentPeriodStartLocalDate,
            PeriodStartUtc = _state.CurrentPeriodStartLocalDate,
            SecondsUntilReset = Math.Max(0, (int)Math.Ceiling((LeaderboardPeriodPolicy.GetNextPeriodStartUtc(now, _leaderboardTimeZone) - now).TotalSeconds)),
            Entries = entries
        };
    }

    public async Task ResetWeeklyIfNeededAsync()
    {
        var now = DateTime.UtcNow;
        EnsurePeriodInitialized(now);
        var currentPeriod = LeaderboardPeriodPolicy.GetCurrentPeriodStartLocalDate(now, _leaderboardTimeZone);
        if (string.Equals(_state.CurrentPeriodStartLocalDate, currentPeriod, StringComparison.Ordinal))
        {
            return;
        }

        var archived = new WeeklyLeaderboardSnapshot
        {
            PeriodStartLocalDate = _state.CurrentPeriodStartLocalDate,
            PeriodStartUtc = _state.CurrentPeriodStartLocalDate,
            Entries = GetRankedEntries().Take(100).ToList()
        };

        if (archived.Entries.Count > 0)
        {
            _state.WeeklySnapshots.Insert(0, archived);
            if (_state.WeeklySnapshots.Count > 2)
            {
                _state.WeeklySnapshots.RemoveRange(2, _state.WeeklySnapshots.Count - 2);
            }
        }

        var playerIds = _state.Players.Keys.ToArray();
        foreach (var playerId in playerIds)
        {
            await Context.Runtime.TellAsync<UserActor>(
                ActorId.From($"user/{playerId}"),
                static (actor, _) =>
                {
                    return new ValueTask(actor.ResetVictoryPointsAsync());
                }).ConfigureAwait(false);
        }

        _state.Players.Clear();
        _state.CurrentPeriodStartLocalDate = currentPeriod;
        _state.CurrentPeriodStartUtc = currentPeriod;
    }

    public async Task RecordVictoryPointsAsync(string playerId, int victoryPoints, int winCount)
    {
        if (string.IsNullOrWhiteSpace(playerId) || victoryPoints <= 0)
        {
            return;
        }

        await ResetWeeklyIfNeededAsync().ConfigureAwait(false);

        if (!_state.Players.TryGetValue(playerId, out var player))
        {
            player = new LeaderboardPlayerState { PlayerId = playerId };
            _state.Players[playerId] = player;
        }

        player.VictoryPoints = Math.Max(0, victoryPoints);
        player.WinCount = Math.Max(0, winCount);
    }

    private List<LeaderboardEntrySnapshot> GetRankedEntries()
    {
        return LeaderboardRankingPolicy.GetRankedEntries(_state.Players.Values);
    }

    private void EnsurePeriodInitialized(DateTime now)
    {
        if (string.IsNullOrWhiteSpace(_state.CurrentPeriodStartLocalDate)
            && !string.IsNullOrWhiteSpace(_state.CurrentPeriodStartUtc))
        {
            _state.CurrentPeriodStartLocalDate = LeaderboardPeriodPolicy.MigrateLegacyPeriodStartUtc(
                _state.CurrentPeriodStartUtc,
                now,
                _leaderboardTimeZone);
        }

        if (string.IsNullOrWhiteSpace(_state.CurrentPeriodStartLocalDate))
        {
            _state.CurrentPeriodStartLocalDate = LeaderboardPeriodPolicy.GetCurrentPeriodStartLocalDate(now, _leaderboardTimeZone);
        }

        _state.CurrentPeriodStartUtc = _state.CurrentPeriodStartLocalDate;
    }
}

public static class LeaderboardRankingPolicy
{
    public static List<LeaderboardEntrySnapshot> GetRankedEntries(IEnumerable<LeaderboardPlayerState> players)
    {
        return players
            .Where(static player => player.VictoryPoints > 0)
            .OrderByDescending(static player => player.VictoryPoints)
            .ThenByDescending(static player => player.WinCount)
            .ThenBy(static player => player.PlayerId, StringComparer.Ordinal)
            .Select((player, index) => new LeaderboardEntrySnapshot
            {
                PlayerId = player.PlayerId,
                VictoryPoints = player.VictoryPoints,
                WinCount = player.WinCount,
                Rank = index + 1
            })
            .ToList();
    }
}

public static class LeaderboardPeriodPolicy
{
    public static string GetCurrentPeriodStartLocalDate(DateTime utcNow, TimeZoneInfo timeZone)
    {
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(NormalizeUtc(utcNow), timeZone);
        var localDate = localNow.Date;
        var daysSinceMonday = ((int)localDate.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return localDate.AddDays(-daysSinceMonday).ToString("yyyy-MM-dd");
    }

    public static DateTime GetNextPeriodStartUtc(DateTime utcNow, TimeZoneInfo timeZone)
    {
        var currentLocalStart = DateTime.ParseExact(
            GetCurrentPeriodStartLocalDate(utcNow, timeZone),
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None);
        var nextLocalStart = DateTime.SpecifyKind(currentLocalStart.AddDays(7), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(nextLocalStart, timeZone);
    }

    public static WeeklyLeaderboardSnapshot? ResetWeeklyIfNeeded(LeaderboardState state, DateTime utcNow, TimeZoneInfo timeZone)
    {
        EnsurePeriodInitialized(state, utcNow, timeZone);
        var currentPeriod = GetCurrentPeriodStartLocalDate(utcNow, timeZone);
        if (string.Equals(state.CurrentPeriodStartLocalDate, currentPeriod, StringComparison.Ordinal))
        {
            return null;
        }

        var archived = new WeeklyLeaderboardSnapshot
        {
            PeriodStartLocalDate = state.CurrentPeriodStartLocalDate,
            PeriodStartUtc = state.CurrentPeriodStartLocalDate,
            Entries = LeaderboardRankingPolicy.GetRankedEntries(state.Players.Values).Take(100).ToList()
        };

        if (archived.Entries.Count > 0)
        {
            state.WeeklySnapshots.Insert(0, archived);
            if (state.WeeklySnapshots.Count > 2)
            {
                state.WeeklySnapshots.RemoveRange(2, state.WeeklySnapshots.Count - 2);
            }
        }

        state.Players.Clear();
        state.CurrentPeriodStartLocalDate = currentPeriod;
        state.CurrentPeriodStartUtc = currentPeriod;
        return archived.Entries.Count > 0 ? archived : null;
    }

    public static string MigrateLegacyPeriodStartUtc(string legacyPeriodStartUtc, DateTime utcNow, TimeZoneInfo timeZone)
    {
        var normalizedUtc = NormalizeUtc(utcNow);
        var utcDate = normalizedUtc.Date;
        var daysSinceMonday = ((int)utcDate.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var currentUtcPeriodStart = utcDate.AddDays(-daysSinceMonday).ToString("yyyy-MM-dd");
        if (string.Equals(legacyPeriodStartUtc, currentUtcPeriodStart, StringComparison.Ordinal))
        {
            return GetCurrentPeriodStartLocalDate(normalizedUtc, timeZone);
        }

        return legacyPeriodStartUtc;
    }

    private static void EnsurePeriodInitialized(LeaderboardState state, DateTime utcNow, TimeZoneInfo timeZone)
    {
        if (string.IsNullOrWhiteSpace(state.CurrentPeriodStartLocalDate)
            && !string.IsNullOrWhiteSpace(state.CurrentPeriodStartUtc))
        {
            state.CurrentPeriodStartLocalDate = MigrateLegacyPeriodStartUtc(state.CurrentPeriodStartUtc, utcNow, timeZone);
        }

        if (string.IsNullOrWhiteSpace(state.CurrentPeriodStartLocalDate))
        {
            state.CurrentPeriodStartLocalDate = GetCurrentPeriodStartLocalDate(utcNow, timeZone);
        }

        state.CurrentPeriodStartUtc = state.CurrentPeriodStartLocalDate;
    }

    private static DateTime NormalizeUtc(DateTime utcNow)
    {
        return utcNow.Kind == DateTimeKind.Utc
            ? utcNow
            : DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    }
}
