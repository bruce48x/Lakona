using Agar.Sample.State.Contracts.Leaderboard;
using Agar.Sample.State.Contracts.Users;
using Agar.Sample.State.Leaderboard;
using Agar.Sample.State.Users;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
using Server.Hotfix.State.Users;

namespace Server.Hotfix.State.Leaderboard;

[HotfixBehaviorOf(typeof(LeaderboardActor))]
public static class LeaderboardBehavior
{
    public static async ValueTask<LeaderboardSnapshot> GetLeaderboardAsync(this LeaderboardActor self, LeaderboardQueryRequest request)
    {
        await ResetWeeklyIfNeededAsync(self, new LeaderboardResetRequest()).ConfigureAwait(false);

        var topN = Math.Clamp(request.TopN, 1, 100);
        var now = DateTime.UtcNow;
        var entries = GetRankedEntries(self)
            .Take(topN)
            .ToList();

        return new LeaderboardSnapshot
        {
            PeriodStartLocalDate = self.State.CurrentPeriodStartLocalDate,
            PeriodStartUtc = self.State.CurrentPeriodStartLocalDate,
            SecondsUntilReset = Math.Max(0, (int)Math.Ceiling((LeaderboardPeriodPolicy.GetNextPeriodStartUtc(now, self.LeaderboardTimeZone) - now).TotalSeconds)),
            Entries = entries
        };
    }

    public static async ValueTask ResetWeeklyIfNeededAsync(this LeaderboardActor self, LeaderboardResetRequest request)
    {
        var now = DateTime.UtcNow;
        EnsurePeriodInitialized(self, now);
        var currentPeriod = LeaderboardPeriodPolicy.GetCurrentPeriodStartLocalDate(now, self.LeaderboardTimeZone);
        if (string.Equals(self.State.CurrentPeriodStartLocalDate, currentPeriod, StringComparison.Ordinal))
        {
            return;
        }

        var archived = new WeeklyLeaderboardSnapshot
        {
            PeriodStartLocalDate = self.State.CurrentPeriodStartLocalDate,
            PeriodStartUtc = self.State.CurrentPeriodStartLocalDate,
            Entries = GetRankedEntries(self).Take(100).ToList()
        };

        if (archived.Entries.Count > 0)
        {
            self.State.WeeklySnapshots.Insert(0, archived);
            if (self.State.WeeklySnapshots.Count > 2)
            {
                self.State.WeeklySnapshots.RemoveRange(2, self.State.WeeklySnapshots.Count - 2);
            }
        }

        var playerIds = self.State.Players.Keys.ToArray();
        var localActors = self.Context.Runtime;
        foreach (var playerId in playerIds)
        {
            await localActors.TellAsync<UserActor>(
                ActorId.From(playerId),
                static (actor, _) => actor.ResetVictoryPointsAsync(new UserVictoryPointsResetRequest())).ConfigureAwait(false);
        }

        self.State.Players.Clear();
        self.State.CurrentPeriodStartLocalDate = currentPeriod;
        self.State.CurrentPeriodStartUtc = currentPeriod;
    }

    public static async ValueTask RecordVictoryPointsAsync(this LeaderboardActor self, LeaderboardVictoryPointsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PlayerId) || request.VictoryPoints <= 0)
        {
            return;
        }

        await ResetWeeklyIfNeededAsync(self, new LeaderboardResetRequest()).ConfigureAwait(false);

        if (!self.State.Players.TryGetValue(request.PlayerId, out var player))
        {
            player = new LeaderboardPlayerState { PlayerId = request.PlayerId };
            self.State.Players[request.PlayerId] = player;
        }

        player.VictoryPoints = Math.Max(0, request.VictoryPoints);
        player.WinCount = Math.Max(0, request.WinCount);
    }

    private static List<LeaderboardEntrySnapshot> GetRankedEntries(LeaderboardActor self)
    {
        return LeaderboardRankingPolicy.GetRankedEntries(self.State.Players.Values);
    }

    private static void EnsurePeriodInitialized(LeaderboardActor self, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(self.State.CurrentPeriodStartLocalDate)
            && !string.IsNullOrWhiteSpace(self.State.CurrentPeriodStartUtc))
        {
            self.State.CurrentPeriodStartLocalDate = LeaderboardPeriodPolicy.MigrateLegacyPeriodStartUtc(
                self.State.CurrentPeriodStartUtc,
                now,
                self.LeaderboardTimeZone);
        }

        if (string.IsNullOrWhiteSpace(self.State.CurrentPeriodStartLocalDate))
        {
            self.State.CurrentPeriodStartLocalDate = LeaderboardPeriodPolicy.GetCurrentPeriodStartLocalDate(now, self.LeaderboardTimeZone);
        }

        self.State.CurrentPeriodStartUtc = self.State.CurrentPeriodStartLocalDate;
    }
}
