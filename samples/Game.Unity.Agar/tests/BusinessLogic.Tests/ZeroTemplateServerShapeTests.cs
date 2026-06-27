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
    public void Program_is_strict_zero_template()
    {
        var program = File.ReadAllText(Path.Combine(ServerApp, "Program.cs"));

        Assert.Equal(
            "using Lakona.Game.Server.Hosting;\n\nreturn await LakonaGameServer.RunAsync(args);\n",
            program);
    }

    [Fact]
    public void ServerApp_does_not_contain_agar_business_directories()
    {
        var forbiddenDirectories = new[]
        {
            "Hosting",
            "Services",
            "Realtime",
            "Features",
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
        var state = Path.Combine(ServerApp, "State");
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "Users/UserActor.cs",
            "Rooms/RoomActor.cs",
            "Matchmaking/MatchmakingActor.cs",
            "Leaderboard/LeaderboardActor.cs"
        };

        var files = Directory.Exists(state)
            ? Directory.GetFiles(state, "*.cs", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(state, path).Replace('\\', '/'))
                .ToArray()
            : Array.Empty<string>();

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
    public void Data_node_does_not_enable_database_as_application_feature()
    {
        var compose = File.ReadAllText(Path.Combine(
            Root,
            "samples",
            "Game.Unity.Agar",
            "docker-compose.yml"));
        var data = ExtractComposeService(compose, "data-1");

        Assert.DoesNotContain("database", data, StringComparison.Ordinal);
        Assert.Contains("Lakona__Feature: '[\"state-store\",\"matchmaking\",\"leaderboard\"]'", data, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__Feature__", data, StringComparison.Ordinal);
    }

    [Fact]
    public void Data_node_splits_cluster_directory_from_agar_persistence()
    {
        var compose = File.ReadAllText(Path.Combine(
            Root,
            "samples",
            "Game.Unity.Agar",
            "docker-compose.yml"));
        var data = ExtractComposeService(compose, "data-1");

        Assert.Contains("ConnectionStrings__LakonaClusterPostgres:", data, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__AgarGamePostgres:", data, StringComparison.Ordinal);
        Assert.Contains("Lakona__Cluster__Directory__Provider: postgres", data, StringComparison.Ordinal);
        Assert.Contains("Lakona__Cluster__Directory__ConnectionStringName: LakonaClusterPostgres", data, StringComparison.Ordinal);
        Assert.Contains("Lakona__Cluster__Directory__NodeTable: lakona_cluster_nodes", data, StringComparison.Ordinal);
        Assert.Contains("Agar__Persistence__Provider: postgres", data, StringComparison.Ordinal);
        Assert.Contains("Agar__Persistence__ConnectionStringName: AgarGamePostgres", data, StringComparison.Ordinal);
        Assert.DoesNotContain("Agar__Database", data, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerApp_does_not_keep_node_specific_appsettings_files()
    {
        Assert.Empty(Directory.GetFiles(ServerApp, "appsettings.*.json"));
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
