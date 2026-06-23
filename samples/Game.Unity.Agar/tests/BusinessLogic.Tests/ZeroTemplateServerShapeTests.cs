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
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            ServerApp,
            "appsettings.data-1.json")));

        var lakona = document.RootElement.GetProperty("Lakona");
        var features = lakona.GetProperty("Feature")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();

        Assert.DoesNotContain("database", features);
        Assert.Contains("state-store", features);
        Assert.Contains("matchmaking", features);
        Assert.Contains("leaderboard", features);
    }

    [Fact]
    public void Data_node_splits_cluster_directory_from_agar_persistence()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            ServerApp,
            "appsettings.data-1.json")));

        var root = document.RootElement;
        Assert.True(root.GetProperty("ConnectionStrings").TryGetProperty("LakonaClusterPostgres", out _));
        Assert.True(root.GetProperty("ConnectionStrings").TryGetProperty("AgarGamePostgres", out _));

        var directory = root
            .GetProperty("Lakona")
            .GetProperty("Cluster")
            .GetProperty("Directory");
        Assert.Equal("postgres", directory.GetProperty("Provider").GetString());
        Assert.Equal("LakonaClusterPostgres", directory.GetProperty("ConnectionStringName").GetString());
        Assert.Equal("lakona_cluster_nodes", directory.GetProperty("NodeTable").GetString());

        var persistence = root
            .GetProperty("Agar")
            .GetProperty("Persistence");
        Assert.Equal("postgres", persistence.GetProperty("Provider").GetString());
        Assert.Equal("AgarGamePostgres", persistence.GetProperty("ConnectionStringName").GetString());
        Assert.False(root.TryGetProperty("Agar:" + "Database", out _));
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
}
