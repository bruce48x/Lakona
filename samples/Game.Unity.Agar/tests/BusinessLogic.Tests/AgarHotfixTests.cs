using Shared.Gameplay;
using Lakona.Game.Server;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Loading;
using Microsoft.Extensions.DependencyInjection;
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
