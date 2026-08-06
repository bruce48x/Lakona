using Server.App.Routing;
using Server.App.Leaderboard;
using Server.App.Users;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Server.Hotfix.Users;
using Shared.Interfaces;

namespace Server.Hotfix.Leaderboard;

[HotfixBehaviorOf(typeof(LeaderboardActor))]
public sealed partial class LeaderboardBehavior
{
    private readonly ActorAccess _actors;
    private readonly ILeaderboardStore _leaderboards;
    private readonly IUserStore _userStore;

    public LeaderboardBehavior(
        ActorAccess actors,
        ILeaderboardStore leaderboards,
        IUserStore userStore)
    {
        _actors = actors;
        _leaderboards = leaderboards;
        _userStore = userStore;
    }

    public async ValueTask<LeaderboardSnapshot> GetLeaderboardAsync(LeaderboardActor self, LeaderboardQueryRequest request, CancellationToken cancellationToken = default)
    {
        await ResetWeeklyIfNeededAsync(
                self,
                new LeaderboardResetRequest(),
                cancellationToken)
            .ConfigureAwait(false);

        var topN = Math.Clamp(request.TopN, 1, 100);
        var now = DateTime.UtcNow;
        var currentPeriod = LeaderboardPeriodPolicy.GetCurrentPeriodStartLocalDate(
            now,
            self.LeaderboardTimeZone);
        var players = await _leaderboards
            .LoadPlayersAsync(currentPeriod, cancellationToken)
            .ConfigureAwait(false);
        var entries = LeaderboardRankingPolicy
            .GetRankedEntries(players)
            .Take(topN)
            .ToList();

        return new LeaderboardSnapshot
        {
            PeriodStartLocalDate = currentPeriod,
            PeriodStartUtc = currentPeriod,
            SecondsUntilReset = Math.Max(0, (int)Math.Ceiling((LeaderboardPeriodPolicy.GetNextPeriodStartUtc(now, self.LeaderboardTimeZone) - now).TotalSeconds)),
            Entries = entries
        };
    }

    public async ValueTask ResetWeeklyIfNeededAsync(LeaderboardActor self, LeaderboardResetRequest request, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var currentPeriod = LeaderboardPeriodPolicy.GetCurrentPeriodStartLocalDate(now, self.LeaderboardTimeZone);
        var persistedPeriod = await _leaderboards
            .GetCurrentPeriodAsync(cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(persistedPeriod))
        {
            await _leaderboards
                .SetCurrentPeriodAsync(currentPeriod, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (string.Equals(persistedPeriod, currentPeriod, StringComparison.Ordinal))
        {
            return;
        }

        var previousPlayers = await _leaderboards
            .LoadPlayersAsync(persistedPeriod, cancellationToken)
            .ConfigureAwait(false);
        foreach (var player in previousPlayers)
        {
            try
            {
                await _actors
                    .Route<UserActor>(new UserId(player.PlayerId))
                    .CallAsync(
                        static behavior => behavior.ResetVictoryPointsAsync,
                        new UserVictoryPointsResetRequest(),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ActorNotFoundException)
            {
                var persistedUser = await _userStore
                    .LoadAsync(player.PlayerId, cancellationToken)
                    .ConfigureAwait(false);
                if (persistedUser is null)
                {
                    continue;
                }

                persistedUser.VictoryPoints = 0;
                await _userStore
                    .SaveAsync(persistedUser, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        await _leaderboards
            .SetCurrentPeriodAsync(currentPeriod, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask RecordVictoryPointsAsync(LeaderboardActor self, LeaderboardVictoryPointsRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PlayerId) || request.VictoryPoints <= 0)
        {
            return;
        }

        await ResetWeeklyIfNeededAsync(self, new LeaderboardResetRequest()).ConfigureAwait(false);
        var currentPeriod = LeaderboardPeriodPolicy.GetCurrentPeriodStartLocalDate(
            DateTime.UtcNow,
            self.LeaderboardTimeZone);
        await _leaderboards
            .UpsertPlayerAsync(
                currentPeriod,
                new LeaderboardPlayerState
                {
                    PlayerId = request.PlayerId,
                    VictoryPoints = Math.Max(0, request.VictoryPoints),
                    WinCount = Math.Max(0, request.WinCount)
                },
                cancellationToken)
            .ConfigureAwait(false);
    }
}
