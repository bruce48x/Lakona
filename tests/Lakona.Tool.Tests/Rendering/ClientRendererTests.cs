using System.Text.Json;
using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Client;
using Xunit;

namespace Lakona.Tool.Tests.Rendering;

public sealed class ClientRendererTests
{
    [Fact]
    public void UnityClientRenderer_EmitsSceneFirstProceduralArenaWithoutArtAssets()
    {
        var plan = Render(new UnityClientRenderer(), Spec(ClientEngine.Unity, TransportKind.Kcp, SerializerKind.MemoryPack));

        AssertPath(plan, "Client/Assets/Scenes/Game.unity");
        AssertPath(plan, "Client/Assets/UI/Game.uxml");
        AssertPath(plan, "Client/Assets/UI/Game.uss");
        var controller = AssertPath(plan, "Client/Assets/Scripts/Game/GameController.cs").Content;
        Assert.Contains("private bool _loginPending", controller, StringComparison.Ordinal);
        Assert.Contains("while (_client.TryDequeueSnapshot", controller, StringComparison.Ordinal);
        Assert.Contains("_client.RefreshWorldAsync", controller, StringComparison.Ordinal);
        Assert.Contains("Input.GetAxisRaw(\"Horizontal\")", controller, StringComparison.Ordinal);
        Assert.Contains("context.painter2D", controller, StringComparison.Ordinal);
        Assert.Contains("DrawCircle", controller, StringComparison.Ordinal);
        Assert.Contains("DrawSegmentedHealth", controller, StringComparison.Ordinal);
        Assert.Contains("DrawDemoBattle", controller, StringComparison.Ordinal);
        Assert.Contains("GenerateArenaVisualContent", controller, StringComparison.Ordinal);
        Assert.Contains("ApplyWorldSnapshot", controller, StringComparison.Ordinal);
        Assert.Contains("HitEffectDuration", controller, StringComparison.Ordinal);
        Assert.Contains("2166136261", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("new Color(0.2f, 0.9f, 0.3f)", PlayerPaletteSource(controller), StringComparison.Ordinal);
        Assert.Contains("CONNECTING...", controller, StringComparison.Ordinal);
        Assert.Contains("_loginPanel.style.display", controller, StringComparison.Ordinal);

        var scene = AssertPath(plan, "Client/Assets/Scenes/Game.unity").Content;
        Assert.Contains("m_Name: Lakona Arena Game", scene, StringComparison.Ordinal);
        Assert.Contains(UnityClientAssetTemplates.GameControllerGuid, scene, StringComparison.Ordinal);
        Assert.Contains(UnityClientAssetTemplates.GameUxmlGuid, scene, StringComparison.Ordinal);
        Assert.Contains("name=\"login-panel\"", AssertPath(plan, "Client/Assets/UI/Game.uxml").Content, StringComparison.Ordinal);
        Assert.Contains("name=\"hud\"", AssertPath(plan, "Client/Assets/UI/Game.uxml").Content, StringComparison.Ordinal);
        Assert.Contains("name=\"health-fill\"", AssertPath(plan, "Client/Assets/UI/Game.uxml").Content, StringComparison.Ordinal);
        var style = AssertPath(plan, "Client/Assets/UI/Game.uss").Content;
        Assert.Contains(".name-field .unity-text-field__input", style, StringComparison.Ordinal);
        Assert.Contains(".title-primary", style, StringComparison.Ordinal);
        Assert.Contains("background-color: rgb(190, 226, 28)", style, StringComparison.Ordinal);
        Assert.Contains("background-color: rgb(255, 76, 64)", style, StringComparison.Ordinal);
        Assert.DoesNotContain(plan.Files, file => IsExternalArt(file.RelativePath));
        Assert.DoesNotContain(plan.Files, file => file.RelativePath.Contains("Chat", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains("Chat", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Tcp", "Json", "new TcpTransport(_serverHost, _serverPort)", "new JsonRpcSerializer()")]
    [InlineData("WebSocket", "MemoryPack", "new WsTransport", "new MemoryPackRpcSerializer()")]
    [InlineData("Kcp", "MemoryPack", "new KcpTransport(_serverHost, _serverPort)", "new MemoryPackRpcSerializer()")]
    public void UnityClientRenderer_UsesSelectedTransportAndSerializer(string transportName, string serializerName, string transportText, string serializerText)
    {
        var plan = Render(new UnityClientRenderer(), Spec(ClientEngine.Unity, Enum.Parse<TransportKind>(transportName), Enum.Parse<SerializerKind>(serializerName)));
        var controller = AssertPath(plan, "Client/Assets/Scripts/Game/GameController.cs").Content;
        Assert.Contains(transportText, controller, StringComparison.Ordinal);
        Assert.Contains(serializerText, controller, StringComparison.Ordinal);
    }

    [Fact]
    public void GodotClientRenderer_EmitsFileBackedProceduralArenaWithoutArtAssets()
    {
        var plan = Render(new GodotClientRenderer(), Spec(ClientEngine.Godot, TransportKind.WebSocket, SerializerKind.Json));
        AssertPath(plan, "Client/Game.tscn");
        var scene = AssertPath(plan, "Client/Game.tscn").Content;
        Assert.Contains("[node name=\"LoginPanel\"", scene, StringComparison.Ordinal);
        Assert.Contains("[node name=\"Hud\"", scene, StringComparison.Ordinal);
        Assert.Contains("res://Scripts/Game/GameScene.cs", scene, StringComparison.Ordinal);

        var code = AssertPath(plan, "Client/Scripts/Game/GameScene.cs").Content;
        Assert.Contains("public override void _Draw()", code, StringComparison.Ordinal);
        Assert.Contains("DrawCircle", code, StringComparison.Ordinal);
        Assert.Contains("DrawSegmentedHealth", code, StringComparison.Ordinal);
        Assert.Contains("DrawDemoBattle", code, StringComparison.Ordinal);
        Assert.Contains("ApplyWorldSnapshot", code, StringComparison.Ordinal);
        Assert.Contains("HitEffectDuration", code, StringComparison.Ordinal);
        Assert.Contains("Input.IsKeyPressed(Key.W)", code, StringComparison.Ordinal);
        Assert.Contains("while (_client.TryDequeueSnapshot", code, StringComparison.Ordinal);
        Assert.Contains("_client.RefreshWorldAsync", code, StringComparison.Ordinal);
        Assert.Contains("_loginPending = true", code, StringComparison.Ordinal);
        Assert.Contains("new WsTransport", code, StringComparison.Ordinal);
        Assert.Contains("new JsonRpcSerializer()", code, StringComparison.Ordinal);
        Assert.Contains("2166136261", code, StringComparison.Ordinal);
        Assert.Contains("LineEdit/colors/font_color = Color(0, 1, 0.4, 1)", AssertPath(plan, "Client/Theme/LakonaTheme.tres").Content, StringComparison.Ordinal);
        Assert.Contains("ArenaInput/type = \"LineEdit\"", AssertPath(plan, "Client/Theme/LakonaTheme.tres").Content, StringComparison.Ordinal);
        Assert.Contains("ArenaButton/type = \"Button\"", AssertPath(plan, "Client/Theme/LakonaTheme.tres").Content, StringComparison.Ordinal);
        Assert.Contains("ArenaHud/type = \"PanelContainer\"", AssertPath(plan, "Client/Theme/LakonaTheme.tres").Content, StringComparison.Ordinal);
        Assert.DoesNotContain(plan.Files, file => IsExternalArt(file.RelativePath));
        Assert.DoesNotContain(plan.Files, file => file.RelativePath.Contains("Chat", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains("Chat", StringComparison.Ordinal));
    }

    [Fact]
    public void ConsoleClientRenderer_EmitsGameSmokeAndLoadInputScenario()
    {
        var plan = Render(new ConsoleClientRenderer(), Spec(ClientEngine.Console, TransportKind.Kcp, SerializerKind.MemoryPack));
        var program = AssertPath(plan, "Client/Program.cs").Content;
        Assert.Contains("client.Api.Shared.Game", program, StringComparison.Ordinal);
        Assert.Contains("SubmitInputAsync", program, StringComparison.Ordinal);
        Assert.Contains("GetWorldAsync", program, StringComparison.Ordinal);
        Assert.Contains("case \"smoke\"", program, StringComparison.Ordinal);
        Assert.Contains("case \"load\"", program, StringComparison.Ordinal);
        var scenario = AssertPath(plan, "Client/LoadScenarios/GameLoadScenario.cs").Content;
        Assert.Contains("public sealed class GameLoadScenario : ILoadScenario", scenario, StringComparison.Ordinal);
        Assert.Contains("MeasureAsync(\"input\"", scenario, StringComparison.Ordinal);
        Assert.Contains("DirectionX = (float)Math.Cos", scenario, StringComparison.Ordinal);
        Assert.DoesNotContain("Chat", string.Join('\n', plan.Files.Select(file => file.RelativePath)), StringComparison.Ordinal);
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains("Chat", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Unity", "OpenUpm")]
    [InlineData("Unity", "Embedded")]
    [InlineData("UnityCn", "Embedded")]
    [InlineData("Tuanjie", "Embedded")]
    public void UnityClientRenderer_ManifestIsValidJson(string engineName, string sourceName)
    {
        var plan = Render(new UnityClientRenderer(), Spec(Enum.Parse<ClientEngine>(engineName), TransportKind.Kcp, SerializerKind.MemoryPack, Enum.Parse<NuGetForUnitySource>(sourceName)));
        using var document = JsonDocument.Parse(AssertPath(plan, "Client/Packages/manifest.json").Content);
        Assert.True(document.RootElement.GetProperty("dependencies").TryGetProperty("com.unity.modules.uielements", out _));
    }

    [Fact]
    public void UnityClientRenderer_EmbeddedSourceRequestsNuGetForUnityArchive()
    {
        var plan = Render(new UnityClientRenderer(), Spec(ClientEngine.Tuanjie, TransportKind.Kcp, SerializerKind.MemoryPack, NuGetForUnitySource.Embedded));
        var archive = Assert.Single(plan.Archives!);
        Assert.Equal("Client/Packages", archive.RelativeDestinationPath);
        Assert.Contains("m_TuanjieEditorVersion", AssertPath(plan, "Client/ProjectSettings/ProjectVersion.txt").Content, StringComparison.Ordinal);
    }

    private static string PlayerPaletteSource(string source)
    {
        var start = source.IndexOf("private static Color PlayerColor", StringComparison.Ordinal);
        return start < 0 ? source : source[start..];
    }

    private static bool IsExternalArt(string path) =>
        path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".psd", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

    private static GenerationPlan Render(IClientRenderer renderer, LakonaProjectSpec spec)
    {
        var builder = new GenerationPlanBuilder("Root");
        renderer.AddFiles(spec, builder);
        return builder.Build();
    }

    private static LakonaProjectSpec Spec(
        ClientEngine engine,
        TransportKind transport,
        SerializerKind serializer,
        NuGetForUnitySource source = NuGetForUnitySource.OpenUpm) =>
        new LakonaProjectSpecFactory().Create(new NewProjectOptions("MyGame", ".", engine, transport, serializer, PersistenceKind.None, source, DeploymentProfile.None, NewProjectOptionPresence.NuGetForUnitySource));

    private static GeneratedFile AssertPath(GenerationPlan plan, string relativePath) =>
        Assert.Single(plan.Files, file => file.RelativePath == relativePath);
}
