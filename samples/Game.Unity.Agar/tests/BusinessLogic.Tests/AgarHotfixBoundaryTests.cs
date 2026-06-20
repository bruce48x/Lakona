using System.Text.RegularExpressions;
using Xunit;

namespace BusinessLogic.Tests;

public sealed class AgarHotfixBoundaryTests
{
    [Fact]
    public void Hotfix_services_do_not_route_actor_behavior_through_stable_state_store_bridges()
    {
        var servicesRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/Hotfix/Services/PlayerService.cs")
            .DirectoryName!;
        var forbiddenTokens = new[]
        {
            "IUserStateStore",
            "IPlayerSessionStateStore",
            "IMatchmakingStateStore",
            "IRoomStateStore",
            "ILeaderboardStateStore",
            "services.Users",
            "services.Sessions",
            "services.Rooms",
            "services.Leaderboard",
        };
        var violations = new List<string>();

        foreach (var file in Directory.GetFiles(servicesRoot, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(file);
            var matches = forbiddenTokens
                .Where(token => text.Contains(token, StringComparison.Ordinal))
                .ToArray();

            if (matches.Length > 0)
            {
                violations.Add($"{Path.GetRelativePath(Directory.GetCurrentDirectory(), file)}: {string.Join(", ", matches)}");
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Hotfix services must dispatch actor behavior directly instead of routing through stable state-store bridges: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Agar_sample_string_named_hotfix_actor_dispatch_stays_inside_stable_state_store_bridge()
    {
        var sampleRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/State/StateStores.cs")
            .Directory!.Parent!.Parent!.Parent!.FullName;
        var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(Path.Combine(sampleRoot, "Server/App/State/StateStores.cs")),
            Path.GetFullPath(Path.Combine(sampleRoot, "Shared/Gameplay/ArenaSimulation.cs")),
        };
        var hotfixDispatch = new Regex(@"HotfixDispatch\.Invoke\s*<", RegexOptions.CultureInvariant);
        var violations = Directory.GetFiles(sampleRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsUnderIgnoredSampleDirectory(sampleRoot, file))
            .Where(file => !allowedFiles.Contains(Path.GetFullPath(file)))
            .Select(file => new
            {
                File = file,
                HasHotfixActorDispatch = hotfixDispatch.IsMatch(File.ReadAllText(file)),
            })
            .Where(result => result.HasHotfixActorDispatch)
            .Select(result => Path.GetRelativePath(Directory.GetCurrentDirectory(), result.File))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Actor hotfix dispatch must stay inside Server/App/State/StateStores.cs. Script hotfix dispatch is only allowed in Shared/Gameplay/ArenaSimulation.cs: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Stable_state_actors_do_not_declare_business_methods()
    {
        var stateRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/State/StateStores.cs")
            .DirectoryName!;
        var actorFiles = Directory.GetFiles(stateRoot, "*Actor.cs", SearchOption.AllDirectories);
        var forbidden = new Regex(
            @"^\s*(public|internal|private|protected)\s+(static\s+|override\s+|async\s+)*(Task|ValueTask|[\w<>]+)\s+\w+\s*\(",
            RegexOptions.Multiline);

        foreach (var file in actorFiles)
        {
            var text = File.ReadAllText(file);
            var matches = forbidden.Matches(text)
                .Where(match => !match.Value.Contains("OnActivateAsync", StringComparison.Ordinal))
                .Where(match => !match.Value.Contains("OnDeactivateAsync", StringComparison.Ordinal))
                .ToArray();

            Assert.True(matches.Length == 0, $"{Path.GetRelativePath(Directory.GetCurrentDirectory(), file)} declares stable actor methods: {string.Join(", ", matches.Select(match => match.Value.Trim()))}");
        }
    }

    private static FileInfo FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return new FileInfo(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }

    private static bool IsUnderIgnoredSampleDirectory(string sampleRoot, string file)
    {
        var parts = Path.GetRelativePath(sampleRoot, file)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var ignoredDirectories = new[]
        {
            "bin",
            "obj",
            "Obj",
            "Library",
            "Temp",
            "Logs",
            "UserSettings",
            "Build",
            "Builds",
            ".godot",
            ".import",
        };

        return parts.Any(part => ignoredDirectories.Contains(part, StringComparer.OrdinalIgnoreCase)) ||
            parts.Length >= 2 &&
            string.Equals(parts[0], "Assets", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(parts[1], "Packages", StringComparison.OrdinalIgnoreCase);
    }
}
