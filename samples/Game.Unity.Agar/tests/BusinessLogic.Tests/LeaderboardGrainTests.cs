using System.Reflection;
using Server.App.State;
using Server.App.State.Contracts.Leaderboard;
using Server.App.State.Contracts.Users;
using Server.App.State.Leaderboard;
using Server.App.State.Users;
using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using Microsoft.Extensions.DependencyInjection;
using Server.Hotfix.State.Leaderboard;
using Server.Hotfix.State.Users;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class LeaderboardActorTests
{
    private static readonly TimeZoneInfo ChinaTimeZone = FindChinaTimeZone();

    [Fact]
    public void RankedEntriesSortByVictoryPointsWinsThenPlayerId()
    {
        var players = new[]
        {
            new LeaderboardPlayerState { PlayerId = "delta", VictoryPoints = 20, WinCount = 1 },
            new LeaderboardPlayerState { PlayerId = "bravo", VictoryPoints = 20, WinCount = 3 },
            new LeaderboardPlayerState { PlayerId = "alpha", VictoryPoints = 20, WinCount = 3 },
            new LeaderboardPlayerState { PlayerId = "charlie", VictoryPoints = 0, WinCount = 99 },
            new LeaderboardPlayerState { PlayerId = "echo", VictoryPoints = 10, WinCount = 10 }
        };

        var ranked = LeaderboardRankingPolicy.GetRankedEntries(players);

        Assert.Collection(
            ranked,
            entry =>
            {
                Assert.Equal("alpha", entry.PlayerId);
                Assert.Equal(1, entry.Rank);
            },
            entry =>
            {
                Assert.Equal("bravo", entry.PlayerId);
                Assert.Equal(2, entry.Rank);
            },
            entry =>
            {
                Assert.Equal("delta", entry.PlayerId);
                Assert.Equal(3, entry.Rank);
            },
            entry =>
            {
                Assert.Equal("echo", entry.PlayerId);
                Assert.Equal(4, entry.Rank);
            });
    }

    [Fact]
    public void PeriodStartUsesLeaderboardLocalMondayInsteadOfUtcMonday()
    {
        var utcNow = new DateTime(2026, 5, 3, 16, 30, 0, DateTimeKind.Utc);

        var periodStart = LeaderboardPeriodPolicy.GetCurrentPeriodStartLocalDate(utcNow, ChinaTimeZone);

        Assert.Equal("2026-05-04", periodStart);
    }

    [Fact]
    public void NextResetUsesLeaderboardLocalMidnight()
    {
        var utcNow = new DateTime(2026, 5, 3, 15, 30, 0, DateTimeKind.Utc);

        var nextResetUtc = LeaderboardPeriodPolicy.GetNextPeriodStartUtc(utcNow, ChinaTimeZone);

        Assert.Equal(new DateTime(2026, 5, 3, 16, 0, 0, DateTimeKind.Utc), nextResetUtc);
    }

    [Fact]
    public void WeeklyResetArchivesTopEntriesAndClearsCurrentPlayers()
    {
        var state = new LeaderboardState
        {
            CurrentPeriodStartLocalDate = "2026-04-27",
            Players =
            {
                ["player-a"] = new LeaderboardPlayerState { PlayerId = "player-a", VictoryPoints = 10, WinCount = 1 },
                ["player-b"] = new LeaderboardPlayerState { PlayerId = "player-b", VictoryPoints = 20, WinCount = 2 }
            }
        };
        var utcNow = new DateTime(2026, 5, 3, 16, 1, 0, DateTimeKind.Utc);

        var archived = LeaderboardPeriodPolicy.ResetWeeklyIfNeeded(state, utcNow, ChinaTimeZone);

        Assert.NotNull(archived);
        Assert.Equal("2026-04-27", archived.PeriodStartLocalDate);
        Assert.Equal("2026-05-04", state.CurrentPeriodStartLocalDate);
        Assert.Empty(state.Players);
        var snapshot = Assert.Single(state.WeeklySnapshots);
        Assert.Equal("player-b", snapshot.Entries[0].PlayerId);
        Assert.Equal("player-a", snapshot.Entries[1].PlayerId);
    }

    [Fact]
    public void WeeklyResetKeepsOnlyTwoArchivedWeeks()
    {
        var state = new LeaderboardState
        {
            CurrentPeriodStartLocalDate = "2026-04-27",
            WeeklySnapshots =
            {
                new WeeklyLeaderboardSnapshot { PeriodStartLocalDate = "2026-04-20" },
                new WeeklyLeaderboardSnapshot { PeriodStartLocalDate = "2026-04-13" }
            },
            Players =
            {
                ["player-a"] = new LeaderboardPlayerState { PlayerId = "player-a", VictoryPoints = 10, WinCount = 1 }
            }
        };
        var utcNow = new DateTime(2026, 5, 3, 16, 1, 0, DateTimeKind.Utc);

        LeaderboardPeriodPolicy.ResetWeeklyIfNeeded(state, utcNow, ChinaTimeZone);

        Assert.Collection(
            state.WeeklySnapshots,
            snapshot => Assert.Equal("2026-04-27", snapshot.PeriodStartLocalDate),
            snapshot => Assert.Equal("2026-04-20", snapshot.PeriodStartLocalDate));
    }

    [Fact]
    public async Task WeeklyResetClearsUserProfileVictoryPoints()
    {
        await TestHotfix.LoadCurrentAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        services.AddGeneratedActorSelectorTestDependencies();

        await using var provider = services.BuildServiceProvider();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var cancellationToken = TestContext.Current.CancellationToken;

        const string userId = "weekly-reset-player";
        await hosting.EnsureAsync<UserActor>(ActorId.From(userId), cancellationToken);
        await hosting.EnsureAsync<LeaderboardActor>(ActorId.From("current"), cancellationToken);
        var login = await actors.AskAsync<UserActor, UserLoginResult>(
            ActorId.From(userId),
            (actor, _) => actor.LoginAsync(new UserLoginRequest { Password = "pw", Reconnect = false }),
            cancellationToken);
        await actors.TellAsync<UserActor>(
            ActorId.From(login.UserId),
            (actor, _) => actor.AddVictoryPointsAsync(new UserVictoryPointsRequest { Points = 25 }),
            cancellationToken);
        var profile = await actors.AskAsync<UserActor, UserProfileSnapshot>(
            ActorId.From(login.UserId),
            (actor, _) => actor.GetProfileAsync(new UserProfileRequest()),
            cancellationToken);
        await actors.TellAsync<LeaderboardActor>(
            ActorId.From("current"),
            (actor, _) => actor.RecordVictoryPointsAsync(new LeaderboardVictoryPointsRequest
            {
                PlayerId = login.UserId,
                VictoryPoints = profile.VictoryPoints,
                WinCount = profile.WinCount
            }),
            cancellationToken);

        await actors.TellAsync<LeaderboardActor>(
            ActorId.From("current"),
            static (actor, _) =>
            {
                var state = GetLeaderboardState(actor);
                state.CurrentPeriodStartLocalDate = "2000-01-03";
                state.CurrentPeriodStartUtc = "2000-01-03";
                return default;
            },
            cancellationToken);

        await actors.AskAsync<LeaderboardActor, LeaderboardSnapshot>(
            ActorId.From("current"),
            (actor, _) => actor.GetLeaderboardAsync(new LeaderboardQueryRequest { TopN = 100 }),
            cancellationToken);

        var resetProfile = await actors.AskAsync<UserActor, UserProfileSnapshot>(
            ActorId.From(login.UserId),
            (actor, _) => actor.GetProfileAsync(new UserProfileRequest()),
            cancellationToken);
        Assert.Equal(0, resetProfile.VictoryPoints);
    }

    [Fact]
    public async Task QueryWithZeroTopNUsesActorClampMinimum()
    {
        await TestHotfix.LoadCurrentAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        services.AddGeneratedActorSelectorTestDependencies();

        await using var provider = services.BuildServiceProvider();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var cancellationToken = TestContext.Current.CancellationToken;

        await hosting.EnsureAsync<LeaderboardActor>(ActorId.From("current"), cancellationToken);
        await actors.TellAsync<LeaderboardActor>(
            ActorId.From("current"),
            (actor, _) => actor.RecordVictoryPointsAsync(new LeaderboardVictoryPointsRequest
            {
                PlayerId = "player-a",
                VictoryPoints = 20,
                WinCount = 1
            }),
            cancellationToken);
        await actors.TellAsync<LeaderboardActor>(
            ActorId.From("current"),
            (actor, _) => actor.RecordVictoryPointsAsync(new LeaderboardVictoryPointsRequest
            {
                PlayerId = "player-b",
                VictoryPoints = 10,
                WinCount = 1
            }),
            cancellationToken);

        var snapshot = await actors.AskAsync<LeaderboardActor, LeaderboardSnapshot>(
            ActorId.From("current"),
            (actor, _) => actor.GetLeaderboardAsync(new LeaderboardQueryRequest { TopN = 0 }),
            cancellationToken);

        var entry = Assert.Single(snapshot.Entries);
        Assert.Equal("player-a", entry.PlayerId);
    }

    [Fact]
    public void LegacyUtcCurrentPeriodMigratesToLocalPeriodBeforeLocalReset()
    {
        var pacificTimeZone = FindPacificTimeZone();
        var utcNow = new DateTime(2026, 5, 4, 1, 0, 0, DateTimeKind.Utc);

        var migrated = LeaderboardPeriodPolicy.MigrateLegacyPeriodStartUtc("2026-05-04", utcNow, pacificTimeZone);

        Assert.Equal("2026-04-27", migrated);
    }

    private static TimeZoneInfo FindChinaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        }
    }

    private static TimeZoneInfo FindPacificTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        }
    }

    private static LeaderboardState GetLeaderboardState(LeaderboardActor actor)
    {
        var field = typeof(LeaderboardActor).GetField(
            "State",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("LeaderboardActor.State field not found.");

        return (LeaderboardState)(field.GetValue(actor)
            ?? throw new InvalidOperationException("LeaderboardActor.State was null."));
    }
}
