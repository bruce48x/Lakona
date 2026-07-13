using System.Text.Json;
using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Server;
using Xunit;

namespace Lakona.Tool.Tests.Rendering;

public sealed class ServerAppRendererTests
{
    [Fact]
    public void AddFiles_EmitsStableGameWorldStateAndCompactSettings()
    {
        var plan = Render(Spec(TransportKind.Kcp, SerializerKind.MemoryPack));

        var program = AssertPath(plan, "Server/App/Program.cs").Content;
        Assert.Equal("using Lakona.Game.Server.Hosting;\n\nreturn await LakonaGameServer.RunAsync(args);", program);

        var actor = AssertPath(plan, "Server/App/Game/GameWorldActor.cs").Content;
        Assert.Contains("internal sealed class GameWorldActor : Actor<string>", actor, StringComparison.Ordinal);
        Assert.Contains("PlayersByName", actor, StringComparison.Ordinal);
        Assert.Contains("List<MonsterState>", actor, StringComparison.Ordinal);
        Assert.Contains("List<BulletState>", actor, StringComparison.Ordinal);
        Assert.Contains("TimerId SimulationTimerId", actor, StringComparison.Ordinal);
        Assert.Contains("string SessionId", actor, StringComparison.Ordinal);
        Assert.Contains("long SessionGeneration", actor, StringComparison.Ordinal);
        Assert.DoesNotContain("GameSessionKey", actor, StringComparison.Ordinal);
        Assert.DoesNotContain("IGameCallback? Callback", actor, StringComparison.Ordinal);
        Assert.DoesNotContain("HotfixBehaviorOf", actor, StringComparison.Ordinal);

        var messages = AssertPath(plan, "Server/App/Game/GameWorldMessages.cs").Content;
        Assert.Contains("MonsterSpawnIntervalSeconds = 3f", messages, StringComparison.Ordinal);
        Assert.Contains("RespawnDelaySeconds = 5f", messages, StringComparison.Ordinal);
        Assert.Contains("MonsterKillScore = 10", messages, StringComparison.Ordinal);
        Assert.Contains("MaxMonsters = 50", messages, StringComparison.Ordinal);
        Assert.Contains("MonsterSpeed = 1.25f", messages, StringComparison.Ordinal);
        Assert.Contains("List<GameWorldRecipient> Recipients", messages, StringComparison.Ordinal);
        Assert.DoesNotContain("GameSessionKey", messages, StringComparison.Ordinal);
        Assert.DoesNotContain("GameSnapshotRequest", messages, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(AssertPath(plan, "Server/App/appsettings.json").Content);
        var lakona = document.RootElement.GetProperty("Lakona");
        Assert.Equal(new[] { "gameWorld" }, lakona.GetProperty("ActorHosts").EnumerateArray().Select(value => value.GetString()).ToArray());
        var managementHttp = lakona.GetProperty("Management").GetProperty("Http");
        Assert.Equal("127.0.0.1", managementHttp.GetProperty("Host").GetString());
        Assert.Equal(20080, managementHttp.GetProperty("Port").GetInt32());
        var health = lakona.GetProperty("Health");
        Assert.True(health.GetProperty("Enabled").GetBoolean());
        Assert.True(health.GetProperty("RequireLoopback").GetBoolean());
        Assert.False(health.TryGetProperty("Http", out _));
        var localAdmin = lakona.GetProperty("Observability").GetProperty("LocalAdmin");
        Assert.True(localAdmin.GetProperty("Enabled").GetBoolean());
        Assert.True(localAdmin.GetProperty("RequireLoopback").GetBoolean());
        var endpoint = lakona.GetProperty("Endpoints")[0];
        Assert.Equal("kcp", endpoint.GetProperty("Transport").GetString());
        Assert.Equal("memorypack", endpoint.GetProperty("Serializer").GetString());
        Assert.Equal(new[] { "game" }, endpoint.GetProperty("RpcServices").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.False(endpoint.TryGetProperty("Name", out _));
    }

    [Fact]
    public void AddFiles_WebSocketSettingsIncludePath()
    {
        var plan = Render(Spec(TransportKind.WebSocket, SerializerKind.Json));
        using var document = JsonDocument.Parse(AssertPath(plan, "Server/App/appsettings.json").Content);
        var endpoint = document.RootElement.GetProperty("Lakona").GetProperty("Endpoints")[0];
        Assert.Equal("/ws", endpoint.GetProperty("Path").GetString());
        Assert.Equal(new[] { "game" }, endpoint.GetProperty("RpcServices").EnumerateArray().Select(value => value.GetString()).ToArray());
    }

    [Fact]
    public void AddFiles_EmitsHotfixBuildTagAndProjectBoundaries()
    {
        var plan = Render(Spec(TransportKind.Kcp, SerializerKind.MemoryPack));
        Assert.Contains("<LakonaHotfixBuildTag>", AssertPath(plan, "Server/App/BuildTag.props").Content, StringComparison.Ordinal);
        var project = AssertPath(plan, "Server/App/Server.App.csproj").Content;
        Assert.Contains("<LakonaHotfixGenerateStableRpcServices>true", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Server.Hotfix.csproj", project, StringComparison.Ordinal);
        Assert.DoesNotContain(plan.Files, file => file.RelativePath.Contains("Generated", StringComparison.Ordinal));
    }

    private static GenerationPlan Render(LakonaProjectSpec spec)
    {
        var builder = new GenerationPlanBuilder("Root");
        new ServerAppRenderer().AddFiles(spec, builder);
        return builder.Build();
    }

    private static LakonaProjectSpec Spec(TransportKind transport, SerializerKind serializer) =>
        new LakonaProjectSpecFactory().Create(new NewProjectOptions("MyGame", ".", ClientEngine.Unity, transport, serializer, PersistenceKind.None, NuGetForUnitySource.OpenUpm, DeploymentProfile.None));

    private static GeneratedFile AssertPath(GenerationPlan plan, string relativePath) =>
        Assert.Single(plan.Files, file => file.RelativePath == relativePath);
}
