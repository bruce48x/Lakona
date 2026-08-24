using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class ZeroTemplateServerShapeTests
{
    private static readonly string Root = FindRepoRoot();
    private static readonly string ServerApp = Path.Combine(
        Root,
        "samples",
        "Game.Unity.Agar",
        "Server",
        "App");

    [Fact]
    public void Program_keeps_only_framework_host_bootstrap()
    {
        var program = File.ReadAllText(Path.Combine(ServerApp, "Program.cs"));

        Assert.Contains("LakonaGameServer.RunAsync", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AgarBattleEndpointAdvertisement", program, StringComparison.Ordinal);
        Assert.DoesNotContain("MatchmakingBehavior", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddAgarPersistence", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AgarPostgresModule", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AgarRedisModule", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerApp_does_not_contain_agar_business_directories()
    {
        var forbiddenDirectories = new[]
        {
            "Hosting",
            "Services",
            "Realtime",
            string.Concat("Fea", "tures"),
            "Hotfix"
        };

        foreach (var directory in forbiddenDirectories)
        {
            Assert.False(
                Directory.Exists(Path.Combine(ServerApp, directory)),
                $"Server.App must not contain Agar business directory '{directory}'.");
        }
    }

    [Fact]
    public void ServerApp_state_contains_only_stable_actor_shells()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "Users/UserActor.cs",
            "Rooms/RoomActor.cs",
            "Matchmaking/MatchmakingActor.cs",
            "Leaderboard/LeaderboardActor.cs"
        };

        var files = Directory.GetFiles(ServerApp, "*Actor.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(ServerApp, path).Replace('\\', '/'))
            .ToArray();

        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            Assert.Contains(file, allowed);
        }
    }

    [Fact]
    public void ServerApp_does_not_contain_removed_agar_symbols()
    {
        var forbiddenSymbols = new[]
        {
            "AddAgar" + "SampleServer",
            "AddAgar" + "SampleActors",
            "AddAgar" + "DatabaseInfrastructure",
            "Session" + "Directory",
            "Player" + "SessionActor",
            "Reliable" + "MatchmakingPublisher",
            "Room" + "CallbackPublisher",
            "Gateway" + "NodeIdentity",
            "Gateway" + "EndpointDescriptorFactory",
            "Battle" + "RuntimeGatewayResolver",
            "Reliable" + "PushKinds",
            "UseGenerated" + "HotfixServices"
        };

        var files = Directory.GetFiles(ServerApp, "*.cs", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var symbol in forbiddenSymbols)
            {
                Assert.DoesNotContain(symbol, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Data_node_does_not_enable_database_as_application_component()
    {
        var compose = File.ReadAllText(Path.Combine(
            Root,
            "samples",
            "Game.Unity.Agar",
            "docker-compose.yml"));
        var data = ExtractComposeService(compose, "data-1");

        Assert.DoesNotContain("database", data, StringComparison.Ordinal);
        Assert.Contains("Lakona__ActorHosts: '[\"user\",\"matchmaking\",\"leaderboard\"]'", data, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__StartupActors", data, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Lakona__", "Fea", "ture"), data, StringComparison.Ordinal);
    }

    [Fact]
    public void AgarHotfixDoesNotUseRemovedAuthoring()
    {
        var hotfixRoot = Path.Combine(
            Root,
            "samples",
            "Game.Unity.Agar",
            "Server",
            "Hotfix");
        var files = Directory.GetFiles(hotfixRoot, "*.cs", SearchOption.AllDirectories);
        var combined = string.Join('\n', files.Select(File.ReadAllText));

        Assert.DoesNotContain(string.Concat("Hotfix", "Fea", "ture"), combined, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("HotfixGame", "Fea", "ture"), combined, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("I", "Fea", "ture", "CommandClient"), combined, StringComparison.Ordinal);
    }

    [Fact]
    public void Agar_nodes_configure_postgres_and_redis_as_stable_dependencies()
    {
        var compose = File.ReadAllText(Path.Combine(
            Root,
            "samples",
            "Game.Unity.Agar",
            "docker-compose.yml"));
        var data = ExtractComposeService(compose, "data-1");

        Assert.Contains("ConnectionStrings__AgarGamePostgres:", data, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__AgarGameRedis:", data, StringComparison.Ordinal);
        Assert.Contains("Lakona__Cluster__Membership__Provider: Postgres", data, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__LakonaClusterPostgres:", data, StringComparison.Ordinal);
        Assert.Contains("Agar__Persistence__Postgres__ConnectionStringName: AgarGamePostgres", data, StringComparison.Ordinal);
        Assert.Contains("Agar__Persistence__Redis__ConnectionStringName: AgarGameRedis", data, StringComparison.Ordinal);
        Assert.DoesNotContain("Agar__Database", data, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerApp_does_not_keep_node_specific_appsettings_files()
    {
        Assert.Empty(Directory.GetFiles(ServerApp, "appsettings.*.json"));
    }

    [Fact]
    public void Readme_build_commands_refresh_hotfix_output_before_running_server()
    {
        var readme = File.ReadAllText(Path.Combine(Root, "samples", "Game.Unity.Agar", "README.md"));

        Assert.Contains("dotnet build Server/Hotfix/Server.Hotfix.csproj", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet build Server/App/Server.App.csproj\ndotnet build Server/App/Server.App.csproj", readme, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Lakona.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find repository root from test base directory.");
    }

    private static string ExtractComposeService(string compose, string serviceName)
    {
        var marker = $"  {serviceName}:";
        var start = compose.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException($"Could not find compose service '{serviceName}'.");
        }

        var next = compose.IndexOf("\n  ", start + marker.Length, StringComparison.Ordinal);
        while (next >= 0)
        {
            var lineEnd = compose.IndexOf('\n', next + 1);
            var line = lineEnd >= 0
                ? compose.Substring(next + 1, lineEnd - next - 1)
                : compose[(next + 1)..];
            if (!line.StartsWith("  ", StringComparison.Ordinal) || line.StartsWith("    ", StringComparison.Ordinal))
            {
                next = compose.IndexOf("\n  ", next + 1, StringComparison.Ordinal);
                continue;
            }

            break;
        }

        return next < 0
            ? compose[start..]
            : compose[start..next];
    }
}
