using Agar.Sample.State.Users;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Loading;
using Shared.Gameplay;

namespace Agar.Unity.Tests;

internal static class TestHotfix
{
    public static async Task LoadCurrentAsync(CancellationToken cancellationToken)
    {
        var hotfixAssemblyPath = FindHotfixAssemblyPath();
        var source = new CurrentDirectoryHotfixAssemblySource(
            Path.GetDirectoryName(hotfixAssemblyPath)!,
            Path.GetFileName(hotfixAssemblyPath));
        var manager = new HotfixManager(source, SharedAssemblyNames());

        var reload = await manager.ReloadAsync(cancellationToken).ConfigureAwait(false);
        if (!reload.Succeeded)
        {
            throw new InvalidOperationException(BuildReloadDiagnostics(reload));
        }
    }

    public static string FindHotfixAssemblyPath(
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

    public static string[] SharedAssemblyNames()
    {
        return
        [
            typeof(ArenaSimulation).Assembly.GetName().Name!,
            typeof(UserActor).Assembly.GetName().Name!,
            typeof(Lakona.Game.Server.ILakonaGameServer).Assembly.GetName().Name!
        ];
    }

    public static string FindRepositoryRoot()
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

    public static string BuildReloadDiagnostics(Lakona.Game.Server.Hotfix.Abstractions.HotfixReloadResult reload)
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

    private static string GetConfigurationName()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }
}
