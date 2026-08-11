using System.Text.Json;
using System.Xml.Linq;
using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Planning;
using Lakona.ProjectSystem.Generation.Rendering.Server;
using Xunit;

namespace Lakona.ProjectSystem.Tests.Rendering;

public sealed class ServerAppRendererTests
{
    [Fact]
    public void AddFiles_EmitsStableGameWorldStateAndCompactSettings()
    {
        var plan = Render(Spec(TransportKind.Kcp, SerializerKind.MemoryPack));

        var program = AssertPath(plan, "Server/App/Program.cs").Content;
        Assert.Contains("using Lakona.Rpc.Serializer.MemoryPack;", program, StringComparison.Ordinal);
        Assert.Contains("using Lakona.Rpc.Transport.Kcp;", program, StringComparison.Ordinal);
        Assert.Contains("using Microsoft.Extensions.Logging;", program, StringComparison.Ordinal);
        Assert.Contains("ConfigureLogging", program, StringComparison.Ordinal);
        Assert.Contains("logging.AddSimpleConsole", program, StringComparison.Ordinal);
        Assert.Contains("RegisterEndpointTransport(\"kcp\"", program, StringComparison.Ordinal);
        Assert.Contains("RegisterEndpointSerializer(\"memorypack\"", program, StringComparison.Ordinal);
        Assert.DoesNotContain("UseClusterRpc", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona.Game.Cluster.Rpc", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona.Rpc.Transport.Tcp", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona.Rpc.Transport.WebSocket", program, StringComparison.Ordinal);

        var project = AssertPath(plan, "Server/App/Server.App.csproj").Content;
        Assert.Contains("Microsoft.Extensions.Logging.Console", project, StringComparison.Ordinal);

        var actor = AssertPath(plan, "Server/App/Game/GameWorldActor.cs").Content;
        Assert.Contains("internal sealed class GameWorldActor : Actor<string>", actor, StringComparison.Ordinal);
        Assert.Contains("PlayersByName", actor, StringComparison.Ordinal);
        Assert.Contains("List<MonsterState>", actor, StringComparison.Ordinal);
        Assert.Contains("List<BulletState>", actor, StringComparison.Ordinal);
        Assert.Contains("TimerId SimulationTimerId", actor, StringComparison.Ordinal);
        Assert.Contains("string SessionId", actor, StringComparison.Ordinal);
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
        Assert.Contains("[MemoryPackable(GenerateType.VersionTolerant)]", messages, StringComparison.Ordinal);
        Assert.Contains("[MemoryPackOrder(0)]", messages, StringComparison.Ordinal);
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
        var lakona = document.RootElement.GetProperty("Lakona");
        var endpoint = lakona.GetProperty("Endpoints")[0];
        Assert.Equal("/ws", endpoint.GetProperty("Path").GetString());
        Assert.Equal(new[] { "game" }, endpoint.GetProperty("RpcServices").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.False(
            lakona.TryGetProperty("Cluster", out var cluster) &&
            cluster.TryGetProperty("Serializer", out _));

        var program = AssertPath(plan, "Server/App/Program.cs").Content;
        Assert.Contains("using Lakona.Rpc.Serializer.Json;", program, StringComparison.Ordinal);
        Assert.Contains("using Lakona.Rpc.Transport.WebSocket;", program, StringComparison.Ordinal);
        Assert.Contains("RegisterEndpointTransport(\"websocket\"", program, StringComparison.Ordinal);
        Assert.Contains("RegisterEndpointSerializer(\"json\"", program, StringComparison.Ordinal);
        Assert.DoesNotContain("UseClusterRpc", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona.Game.Cluster.Rpc", program, StringComparison.Ordinal);

        var messages = AssertPath(plan, "Server/App/Game/GameWorldMessages.cs").Content;
        Assert.Contains("[MemoryPackable(GenerateType.VersionTolerant)]", messages, StringComparison.Ordinal);
    }

    [Fact]
    public void AddFiles_emits_the_shared_alphanumeric_build_tag()
    {
        var plan = Render(Spec(TransportKind.Kcp, SerializerKind.MemoryPack));
        var buildTagProps = AssertPath(plan, "Server/BuildTag.props").Content;
        Assert.Contains("<LakonaBuildTag>Dev1</LakonaBuildTag>", buildTagProps, StringComparison.Ordinal);
        Assert.DoesNotContain("LakonaHotfixBuildTag", buildTagProps, StringComparison.Ordinal);
        var project = AssertPath(plan, "Server/App/Server.App.csproj").Content;
        Assert.Contains("""<Import Project="..\BuildTag.props" />""", project, StringComparison.Ordinal);
        Assert.Contains("<_Parameter1>LakonaBuildTag</_Parameter1>", project, StringComparison.Ordinal);
        Assert.Contains("<_Parameter2>$(LakonaBuildTag)</_Parameter2>", project, StringComparison.Ordinal);
        Assert.Contains("<LakonaProjectRole>ServerApp</LakonaProjectRole>", project, StringComparison.Ordinal);
        Assert.DoesNotContain("LakonaRpcGenerateServer", project, StringComparison.Ordinal);
        Assert.DoesNotContain("LakonaRpcServerGeneratedNamespace", project, StringComparison.Ordinal);
        Assert.DoesNotContain("LakonaHotfixGenerateStableRpcServices", project, StringComparison.Ordinal);
        Assert.DoesNotContain("<CompilerVisibleProperty", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Server.Hotfix.csproj", project, StringComparison.Ordinal);
        Assert.DoesNotContain(plan.Files, file => file.RelativePath.Contains("Generated", StringComparison.Ordinal));
    }

    [Fact]
    public void AddFiles_keeps_build_serialization_without_a_restore_parallelism_override()
    {
        var plan = Render(Spec(TransportKind.Kcp, SerializerKind.MemoryPack));
        var project = AssertPath(plan, "Server/App/Server.App.csproj").Content;

        Assert.Contains("<BuildInParallel>false</BuildInParallel>", project, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreBuildInParallel", project, StringComparison.Ordinal);
    }

    [Fact]
    public void AddFiles_Grants_internal_access_only_to_the_paired_hotfix_assembly()
    {
        var plan = Render(Spec(TransportKind.Kcp, SerializerKind.MemoryPack));
        var project = XDocument.Parse(
            AssertPath(plan, "Server/App/Server.App.csproj").Content);
        var friend = Assert.Single(
            project.Descendants("AssemblyAttribute"),
            static attribute => string.Equals(
                (string?)attribute.Attribute("Include"),
                "System.Runtime.CompilerServices.InternalsVisibleToAttribute",
                StringComparison.Ordinal));

        Assert.Equal(
            "Server.Hotfix",
            friend.Elements("_Parameter1").Single().Value.Trim());
    }

    private static GenerationPlan Render(LakonaProjectSpec spec)
    {
        var builder = new GenerationPlanBuilder("Root");
        new ServerAppRenderer().AddFiles(spec, builder);
        return builder.Build();
    }

    private static LakonaProjectSpec Spec(TransportKind transport, SerializerKind serializer) =>
        new ProjectSpecTestFactory().Create(new ProjectSpecTestOptions("MyGame", ".", ClientEngine.Unity, transport, serializer, NuGetForUnitySource.OpenUpm, DeploymentProfile.None));

    private static GeneratedFile AssertPath(GenerationPlan plan, string relativePath) =>
        Assert.Single(plan.Files, file => file.RelativePath == relativePath);
}
