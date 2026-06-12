using Shared.Gameplay;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Loading;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class AgarHotfixTests
{
    [Fact]
    public void Hotfix_behavior_sources_do_not_use_system_class_names()
    {
        var root = Path.Combine(FindRepositoryRoot(), "samples", "Game.Unity.Agar");
        var hotfixRoots = new[]
        {
            Path.Combine(root, "Server", "Hotfix"),
            Path.Combine(root, "Server", "HotfixV2")
        };

        foreach (var file in hotfixRoots.SelectMany(static path => Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories)))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("System.cs", file, StringComparison.Ordinal);
            Assert.DoesNotMatch("""\bclass\s+\w*System\b""", text);
        }
    }

    [Fact]
    public async Task SettleMatch_uses_hotfix_rule_to_award_winner_points()
    {
        var hotfixAssemblyPath = FindHotfixAssemblyPath();
        var source = new CurrentDirectoryHotfixAssemblySource(
            Path.GetDirectoryName(hotfixAssemblyPath)!,
            Path.GetFileName(hotfixAssemblyPath));
        var manager = new HotfixManager(source, [typeof(ArenaSimulation).Assembly.GetName().Name!]);

        var reload = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(reload.Succeeded, BuildReloadDiagnostics(reload));
        var settleMatchKey = Assert.Single(
            reload.Current.Methods,
            key => key.StateTypeName == typeof(ArenaSimulation).FullName &&
                   key.MethodName == nameof(ArenaSimulation.SettleMatch));
        var settleMatch = HotfixDispatch.Current.Resolve(settleMatchKey);
        Assert.Same(typeof(ArenaSimulation), settleMatch.GetParameters()[0].ParameterType);

        var simulation = new ArenaSimulation(new ArenaSimulationOptions
        {
            EnableBots = false,
            FoodTargetCount = 0
        });
        simulation.UpsertPlayer(new ArenaPlayerRegistration { PlayerId = "p1", Mass = 50 });
        simulation.UpsertPlayer(new ArenaPlayerRegistration { PlayerId = "p2", Mass = 25 });

        var settlement = simulation.SettleMatch(simulation.CreateWorldState());

        Assert.Equal("p1", settlement.WinnerPlayerId);
        Assert.Equal(10, settlement.Entries.Single(entry => entry.PlayerId == "p1").VictoryPoints);
    }

    [Fact]
    public async Task Hotfix_reload_keeps_existing_arena_state()
    {
        var hotfixAssemblyPath = FindHotfixAssemblyPath();
        var source = new CurrentDirectoryHotfixAssemblySource(
            Path.GetDirectoryName(hotfixAssemblyPath)!,
            Path.GetFileName(hotfixAssemblyPath));
        var manager = new HotfixManager(source, [typeof(ArenaSimulation).Assembly.GetName().Name!]);

        var firstReload = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.True(firstReload.Succeeded, BuildReloadDiagnostics(firstReload));

        var simulation = new ArenaSimulation(new ArenaSimulationOptions
        {
            EnableBots = false,
            FoodTargetCount = 0
        });
        simulation.UpsertPlayer(new ArenaPlayerRegistration { PlayerId = "p1", Mass = 50 });
        simulation.UpsertPlayer(new ArenaPlayerRegistration { PlayerId = "p2", Mass = 25 });

        var secondReload = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.True(secondReload.Succeeded, BuildReloadDiagnostics(secondReload));

        Assert.True(simulation.TryGetPlayerSnapshot("p1", out var snapshot));
        Assert.Equal("p1", snapshot.PlayerId);
        Assert.Equal(50, snapshot.Mass);

        var settlement = simulation.SettleMatch(simulation.CreateWorldState());

        Assert.Equal("p1", settlement.WinnerPlayerId);
        Assert.Equal(10, settlement.Entries.Single(entry => entry.PlayerId == "p1").VictoryPoints);
        Assert.Equal(7, settlement.Entries.Single(entry => entry.PlayerId == "p2").VictoryPoints);
    }

    [Fact]
    public async Task Hotfix_reload_applies_v2_arena_rules_to_existing_state()
    {
        var hotfixAssemblyPath = FindHotfixAssemblyPath();
        var hotfixV2AssemblyPath = FindHotfixAssemblyPath("Agar.Sample.Hotfix.V2.dll", "HotfixV2");
        var source = new SwitchableHotfixAssemblySource(hotfixAssemblyPath);
        var manager = new HotfixManager(source, [typeof(ArenaSimulation).Assembly.GetName().Name!]);

        var firstReload = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.True(firstReload.Succeeded, BuildReloadDiagnostics(firstReload));

        var simulation = new ArenaSimulation(new ArenaSimulationOptions
        {
            EnableBots = false,
            FoodTargetCount = 0
        });
        simulation.UpsertPlayer(new ArenaPlayerRegistration { PlayerId = "p1", Mass = 50 });
        simulation.UpsertPlayer(new ArenaPlayerRegistration { PlayerId = "p2", Mass = 25 });

        var v1Settlement = simulation.SettleMatch(simulation.CreateWorldState());
        Assert.Equal(10, v1Settlement.Entries.Single(entry => entry.PlayerId == "p1").VictoryPoints);
        Assert.True(simulation.TryGetPlayerSnapshot("p1", out var snapshotBeforeReload));
        Assert.Equal(50, snapshotBeforeReload.Mass);

        source.Path = hotfixV2AssemblyPath;

        var secondReload = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.True(secondReload.Succeeded, BuildReloadDiagnostics(secondReload));

        Assert.True(simulation.TryGetPlayerSnapshot("p1", out var snapshotAfterReload));
        Assert.Equal(50, snapshotAfterReload.Mass);

        var v2Settlement = simulation.SettleMatch(simulation.CreateWorldState());

        Assert.Equal("p1", v2Settlement.WinnerPlayerId);
        Assert.Equal(20, v2Settlement.Entries.Single(entry => entry.PlayerId == "p1").VictoryPoints);
    }

    [Fact]
    public async Task Hotfix_failed_reload_keeps_previous_arena_rules_and_state()
    {
        var hotfixAssemblyPath = FindHotfixAssemblyPath();
        var source = new SwitchableHotfixAssemblySource(hotfixAssemblyPath);
        var manager = new HotfixManager(source, [typeof(ArenaSimulation).Assembly.GetName().Name!]);

        var firstReload = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.True(firstReload.Succeeded, BuildReloadDiagnostics(firstReload));
        var settleMatchKey = Assert.Single(
            firstReload.Current.Methods,
            key => key.StateTypeName == typeof(ArenaSimulation).FullName &&
                   key.MethodName == nameof(ArenaSimulation.SettleMatch));
        var previousMethod = HotfixDispatch.Current.Resolve(settleMatchKey);

        var simulation = new ArenaSimulation(new ArenaSimulationOptions
        {
            EnableBots = false,
            FoodTargetCount = 0
        });
        simulation.UpsertPlayer(new ArenaPlayerRegistration { PlayerId = "p1", Mass = 50 });
        simulation.UpsertPlayer(new ArenaPlayerRegistration { PlayerId = "p2", Mass = 25 });

        source.Path = Path.Combine(Path.GetTempPath(), "LakonaGameMissingHotfix", "Agar.Sample.Hotfix.dll");

        var failedReload = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.False(failedReload.Succeeded);
        Assert.Equal(firstReload.Current.DispatchTableVersion, failedReload.Current.DispatchTableVersion);
        Assert.Same(previousMethod, HotfixDispatch.Current.Resolve(settleMatchKey));
        Assert.True(simulation.TryGetPlayerSnapshot("p1", out var snapshot));
        Assert.Equal(50, snapshot.Mass);

        var settlement = simulation.SettleMatch(simulation.CreateWorldState());

        Assert.Equal("p1", settlement.WinnerPlayerId);
        Assert.Equal(10, settlement.Entries.Single(entry => entry.PlayerId == "p1").VictoryPoints);
        Assert.Equal(7, settlement.Entries.Single(entry => entry.PlayerId == "p2").VictoryPoints);
    }

    private static string FindHotfixAssemblyPath(
        string assemblyFileName = "Agar.Sample.Hotfix.dll",
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

    private sealed class SwitchableHotfixAssemblySource : IHotfixAssemblySource
    {
        public SwitchableHotfixAssemblySource(string path)
        {
            Path = path;
        }

        public string Path { get; set; }

        public ValueTask<HotfixAssemblySourceResult> ResolveAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new HotfixAssemblySourceResult(
                "switchable",
                "test",
                Path,
                System.IO.Path.GetDirectoryName(Path) ?? Environment.CurrentDirectory));
        }
    }
}
