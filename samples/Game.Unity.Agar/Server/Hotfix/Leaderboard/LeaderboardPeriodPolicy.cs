using Server.App.Leaderboard;

namespace Server.Hotfix.Leaderboard;

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
