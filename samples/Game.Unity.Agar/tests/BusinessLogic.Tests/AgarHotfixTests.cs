using System.Reflection;
using Agar.Sample.State.Contracts.Leaderboard;
using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Contracts.Users;
using Agar.Sample.State.Rooms;
using Agar.Sample.State.Users;
using Agar.Sample.State.Leaderboard;
using Lakona.Game.Server.Actors;
using Shared.Gameplay;
using Lakona.Game.Server;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Loading;
using Microsoft.Extensions.DependencyInjection;
using Server.Hotfix.State.Leaderboard;
using Server.Hotfix.State.Rooms;
using Server.Hotfix.State.Users;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class AgarHotfixTests
{
    [Fact]
    public async Task Hotfix_reload_succeeds_without_cluster_remote_actor_services()
    {
        var hotfixAssemblyPath = FindHotfixAssemblyPath();
        var source = new CurrentDirectoryHotfixAssemblySource(
            Path.GetDirectoryName(hotfixAssemblyPath)!,
            Path.GetFileName(hotfixAssemblyPath));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        new global::GeneratedHotfixActorRegistration().Register(services);
        await using var rootServices = services.BuildServiceProvider();
        var manager = new HotfixManager(source, HotfixSharedAssemblyNames(), rootServices: rootServices);

        var reload = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(reload.Succeeded, BuildReloadDiagnostics(reload));
    }

    [Fact]
    public void Hotfix_behavior_sources_do_not_use_system_class_names()
    {
        var root = Path.Combine(FindRepositoryRoot(), "samples", "Game.Unity.Agar");
        var hotfixRoots = new[]
        {
            Path.Combine(root, "Server", "Hotfix")
        };

        foreach (var file in hotfixRoots.SelectMany(static path => Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories)))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("System.cs", file, StringComparison.Ordinal);
            Assert.DoesNotMatch("""\bclass\s+\w*System\b""", text);
        }
    }

    [Fact]
    public async Task Hotfix_reload_includes_room_behavior_settlement_rules()
    {
        var hotfixAssemblyPath = FindHotfixAssemblyPath();
        var source = new CurrentDirectoryHotfixAssemblySource(
            Path.GetDirectoryName(hotfixAssemblyPath)!,
            Path.GetFileName(hotfixAssemblyPath));
        await using var rootServices = TestHotfix.CreateRootServiceProvider();
        var manager = new HotfixManager(source, HotfixSharedAssemblyNames(), rootServices: rootServices);

        var reload = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(reload.Succeeded, BuildReloadDiagnostics(reload));
        Assert.Contains(
            reload.Current.Methods,
            key => key.StateTypeName == typeof(Agar.Sample.State.Rooms.RoomActor).FullName &&
                   key.MethodName == "TickAsync");
        Assert.DoesNotContain(
            reload.Current.Methods,
            key => key.StateTypeName == typeof(ArenaSimulation).FullName);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        new global::GeneratedHotfixActorRegistration().Register(services);
        services.AddGeneratedActorSelectorTestDependencies();
        var roomNotifierType = typeof(RoomBehavior).Assembly.GetType("Server.Hotfix.Services.RoomNotifier", throwOnError: true)!;
        services.AddSingleton(roomNotifierType);
        await using var behaviorServices = services.BuildServiceProvider();
        var actors = behaviorServices.GetRequiredService<IActorRuntime>();
        var lifecycle = (IActorLifecycle)actors;
        var roomId = "settlement-rules-room";
        var matchId = "settlement-rules-match";
        var players = new[] { "p1", "p2" };

        foreach (var playerId in players)
        {
            await lifecycle.CreateLocalAsync<UserActor>(ActorId.From(playerId), cancellationToken: TestContext.Current.CancellationToken);
            await actors.AskAsync<UserActor, UserLoginResult>(
                ActorId.From(playerId),
                (actor, _) => actor.LoginAsync(new UserLoginRequest
                {
                    Password = "test-password"
                }),
                TestContext.Current.CancellationToken);
        }

        await lifecycle.CreateLocalAsync<RoomActor>(ActorId.From(roomId), cancellationToken: TestContext.Current.CancellationToken);
        await lifecycle.CreateLocalAsync<LeaderboardActor>(ActorId.From("current"), cancellationToken: TestContext.Current.CancellationToken);
        await actors.AskAsync<RoomActor, RoomSettlementResult>(
            ActorId.From(roomId),
            (actor, _) => actor.CreateAsync(new RoomCreateRequest
            {
                RoomId = roomId,
                MatchId = matchId,
                CreatedByUserId = "p1",
                CreatedAtUtc = DateTime.UtcNow,
                MaxPlayers = 2,
                Players =
                [
                    BuildAssignment("p1", roomId, matchId, 0),
                    BuildAssignment("p2", roomId, matchId, 1)
                ]
            }),
            TestContext.Current.CancellationToken);
        await actors.AskAsync<RoomActor, RoomSettlementResult>(
            ActorId.From(roomId),
            (actor, _) => actor.StartAsync(new RoomStartRequest
            {
                RoomId = roomId,
                StartedByUserId = "p1",
                StartedAtUtc = DateTime.UtcNow
            }),
            TestContext.Current.CancellationToken);
        await actors.TellAsync<RoomActor>(
            ActorId.From(roomId),
            (actor, _) =>
            {
                SeedSimulationRankingMasses(actor);
                return default;
            },
            TestContext.Current.CancellationToken);

        await actors.TellAsync<RoomActor>(
            ActorId.From(roomId),
            (actor, _) => actor.TickAsync(new HotfixActorTick
            {
                ObservedAtUtc = DateTime.UtcNow,
                Interval = TimeSpan.FromSeconds(121),
                DispatchTableVersion = reload.Current.DispatchTableVersion
            }),
            TestContext.Current.CancellationToken);

        var room = await actors.AskAsync<RoomActor, RoomSnapshot>(
            ActorId.From(roomId),
            (actor, _) => actor.GetSnapshotAsync(new RoomSnapshotRequest()),
            TestContext.Current.CancellationToken);
        var p1 = await actors.AskAsync<UserActor, UserProfileSnapshot>(
            ActorId.From("p1"),
            (actor, _) => actor.GetProfileAsync(new UserProfileRequest()),
            TestContext.Current.CancellationToken);
        var p2 = await actors.AskAsync<UserActor, UserProfileSnapshot>(
            ActorId.From("p2"),
            (actor, _) => actor.GetProfileAsync(new UserProfileRequest()),
            TestContext.Current.CancellationToken);
        var leaderboard = await actors.AskAsync<LeaderboardActor, LeaderboardSnapshot>(
            ActorId.From("current"),
            (actor, _) => actor.GetLeaderboardAsync(new LeaderboardQueryRequest { TopN = 10 }),
            TestContext.Current.CancellationToken);

        Assert.Equal(RoomStatus.Finished, room.Status);
        Assert.Equal("p1", room.WinnerUserId);
        Assert.Equal(1, room.Players.Single(player => player.UserId == "p1").Rank);
        Assert.Equal(2, room.Players.Single(player => player.UserId == "p2").Rank);
        Assert.Equal(1, p1.WinCount);
        Assert.Equal(10, p1.VictoryPoints);
        Assert.Equal(0, p2.WinCount);
        Assert.Equal(7, p2.VictoryPoints);
        Assert.Equal(
            ["p1", "p2"],
            leaderboard.Entries.Select(entry => entry.PlayerId).ToArray());
        Assert.Equal(10, leaderboard.Entries.Single(entry => entry.PlayerId == "p1").VictoryPoints);
        Assert.Equal(7, leaderboard.Entries.Single(entry => entry.PlayerId == "p2").VictoryPoints);
    }

    private static PlayerRoomAssignment BuildAssignment(string userId, string roomId, string matchId, int seatIndex)
    {
        return new PlayerRoomAssignment
        {
            UserId = userId,
            SessionToken = $"token-{userId}",
            ConnectionId = $"connection-{userId}",
            RoomId = roomId,
            MatchId = matchId,
            SeatIndex = seatIndex,
            AssignedAtUtc = DateTime.UtcNow
        };
    }

    private static void SeedSimulationRankingMasses(RoomActor actor)
    {
        var stateField = typeof(RoomActor).GetField("State", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var state = (RoomState)stateField.GetValue(actor)!;
        foreach (var player in state.Simulation.Players)
        {
            player.Mass = player.PlayerId switch
            {
                "p1" => 50f,
                "p2" => 25f,
                _ => player.Mass
            };
        }
    }

    private static string FindHotfixAssemblyPath(
        string assemblyFileName = "Server.Hotfix.dll",
        string hotfixProjectDirectoryName = "Hotfix")
    {
        var directCandidate = Path.Combine(AppContext.BaseDirectory, assemblyFileName);
        if (File.Exists(directCandidate))
        {
            return directCandidate;
        }

        var root = FindRepositoryRoot();
        var configuration = GetConfigurationName();
        var candidates = new[]
        {
            Path.Combine(root, "samples", "Game.Unity.Agar", "Server", hotfixProjectDirectoryName, "bin", configuration, "net10.0", assemblyFileName),
            Path.Combine(root, "samples", "Game.Unity.Agar", "Server", hotfixProjectDirectoryName, "bin", "Debug", "net10.0", assemblyFileName),
            Path.Combine(root, "samples", "Game.Unity.Agar", "Server", hotfixProjectDirectoryName, "bin", "Release", "net10.0", assemblyFileName)
        };

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"Could not locate {assemblyFileName}. Checked:{Environment.NewLine}{string.Join(Environment.NewLine, candidates.Prepend(directCandidate))}",
            assemblyFileName);
    }

    private static string[] HotfixSharedAssemblyNames()
    {
        return TestHotfix.SharedAssemblyNames();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "samples", "Game.Unity.Agar")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository root from '{AppContext.BaseDirectory}'.");
    }

    private static string GetConfigurationName()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    private static string BuildReloadDiagnostics(Lakona.Game.Server.Hotfix.Abstractions.HotfixReloadResult reload)
    {
        return string.Join(
            Environment.NewLine,
            new[]
            {
                $"Status: {reload.Status}",
                $"RequestedPath: {reload.RequestedPath}",
                $"ErrorMessage: {reload.ErrorMessage}",
                $"ExceptionType: {reload.ExceptionType}",
                "Diagnostics:",
                string.Join(Environment.NewLine, reload.Diagnostics)
            });
    }

}
