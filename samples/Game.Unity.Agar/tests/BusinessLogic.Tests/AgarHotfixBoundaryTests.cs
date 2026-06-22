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
    public void Agar_app_does_not_define_stable_state_store_bridges_or_hand_written_hotfix_dispatch()
    {
        var sampleRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Program.cs")
            .Directory!.Parent!.Parent!.FullName;
        var appRoot = Path.Combine(sampleRoot, "Server", "App");
        var deletedBridge = Path.Combine(appRoot, "State", "StateStores.cs");
        var forbidden = new Regex(
            @"\bI(?:User|PlayerSession|Matchmaking|Room|Leaderboard)StateStore\b|HotfixDispatch\.Invoke\s*<|InvokeServiceAsync\s*<[^;]+,\s*string",
            RegexOptions.CultureInvariant);
        var violations = Directory.GetFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsUnderIgnoredSampleDirectory(sampleRoot, file))
            .Select(file => new
            {
                File = file,
                Matches = forbidden.Matches(File.ReadAllText(file)).Select(match => match.Value).Distinct().ToArray()
            })
            .Where(result => result.Matches.Length > 0)
            .Select(result => $"{Path.GetRelativePath(Directory.GetCurrentDirectory(), result.File)}: {string.Join(", ", result.Matches)}")
            .ToArray();

        Assert.False(File.Exists(deletedBridge), "Server/App/State/StateStores.cs should not exist.");
        Assert.True(
            violations.Length == 0,
            $"Server.App must not define stable state-store bridges or hand-written hotfix dispatch: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Agar_sample_actor_hotfix_dispatch_is_limited_to_scripted_gameplay_extension_points()
    {
        var sampleRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Program.cs")
            .Directory!.Parent!.Parent!.FullName;
        var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
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
            $"Actor hotfix dispatch is only allowed in Shared/Gameplay/ArenaSimulation.cs scripted gameplay extension points: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Stable_state_actors_do_not_declare_business_methods()
    {
        var stateRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/State/AgarSampleActorServiceCollectionExtensions.cs")
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

    [Fact]
    public void Agar_server_app_does_not_contain_user_runtime_features_or_app_hotfix_adapters()
    {
        var appRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Program.cs")
            .DirectoryName!;
        var forbiddenRelativePaths = new[]
        {
            "Features/BattleRuntimeFeature.cs",
            "Features/DatabaseFeature.cs",
            "Features/StateStoreFeature.cs",
            "Features/MatchmakingFeature.cs",
            "Features/LeaderboardFeature.cs",
            "Hosting/MatchmakingHostedService.cs",
            "Hotfix/AgarHotfixRuntimeEvents.cs",
            "Hotfix/AgarRuntimeContracts.cs",
            "Realtime/RoomRuntime.cs",
            "Realtime/RoomRuntimeHost.cs",
        };
        var existingForbiddenPaths = forbiddenRelativePaths
            .Where(path => File.Exists(Path.Combine(appRoot, path)))
            .ToArray();

        Assert.True(
            existingForbiddenPaths.Length == 0,
            $"Server.App must not contain user runtime Feature classes, App hotfix adapters, or runtime loops: {string.Join(", ", existingForbiddenPaths)}");

        var forbidden = new Regex(
            @"\b(Server\.App\.Hotfix|AgarHotfixRuntimeEvents|IAgarRuntimeService|AgarRuntimeMethodIds|RoomRuntimeHost|RoomRuntime|MatchmakingHostedService)\b",
            RegexOptions.CultureInvariant);
        var violations = Directory.GetFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Select(file => new
            {
                File = file,
                Matches = forbidden.Matches(File.ReadAllText(file)).Select(match => match.Value).Distinct().ToArray()
            })
            .Where(result => result.Matches.Length > 0)
            .Select(result => $"{Path.GetRelativePath(Directory.GetCurrentDirectory(), result.File)}: {string.Join(", ", result.Matches)}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Server.App still references removed runtime boundary types: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Agar_user_features_are_hotfix_descriptors()
    {
        var hotfixRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj")
            .DirectoryName!;
        var hotfixText = ReadAllTextFiles(hotfixRoot);

        Assert.Contains("[HotfixFeature(\"database\")]", hotfixText, StringComparison.Ordinal);
        Assert.Contains("[HotfixFeature(\"state-store\")]", hotfixText, StringComparison.Ordinal);
        Assert.Contains("[HotfixFeature(\"matchmaking\")]", hotfixText, StringComparison.Ordinal);
        Assert.Contains("[HotfixFeature(\"leaderboard\")]", hotfixText, StringComparison.Ordinal);
        Assert.Contains("[HotfixFeature(\"battle-runtime\")]", hotfixText, StringComparison.Ordinal);
        Assert.Contains("ScheduleActorTick<MatchmakingActor>", hotfixText, StringComparison.Ordinal);
        Assert.Contains("ScheduleActiveActorTicks<RoomActor>", hotfixText, StringComparison.Ordinal);
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

    private static string ReadAllTextFiles(string root)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Where(IsTextSourceFile)
                     .Order(StringComparer.Ordinal))
        {
            builder.AppendLine(File.ReadAllText(path));
        }

        return builder.ToString();
    }

    private static bool IsTextSourceFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension is ".cs" or ".csproj" or ".json" or ".slnx" or ".props" or ".xml" or ".txt";
    }
}
