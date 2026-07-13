using System.Text.Json;
using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Client;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
        var playerSettings = AssertPath(plan, "Client/ProjectSettings/ProjectSettings.asset").Content;
        Assert.Contains("defaultScreenWidth: 800", playerSettings, StringComparison.Ordinal);
        Assert.Contains("defaultScreenHeight: 600", playerSettings, StringComparison.Ordinal);
        Assert.Contains("fullscreenMode: 3", playerSettings, StringComparison.Ordinal);
        Assert.Contains("resizableWindow: 1", playerSettings, StringComparison.Ordinal);
        Assert.Contains("allowFullscreenSwitch: 1", playerSettings, StringComparison.Ordinal);
        Assert.Contains("useFlipModelSwapchain: 1", playerSettings, StringComparison.Ordinal);
        Assert.Contains("activeInputHandler: 1", playerSettings, StringComparison.Ordinal);
        var controller = AssertPath(plan, "Client/Assets/Scripts/Game/GameController.cs").Content;
        AssertValidCSharp(controller, LanguageVersion.CSharp9);
        Assert.Contains("private bool _loginPending", controller, StringComparison.Ordinal);
        Assert.Contains("while (_client.TryDequeueSnapshot", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshWorldAsync", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("GetWorldAsync", controller, StringComparison.Ordinal);
        Assert.Contains("using UnityEngine.InputSystem;", controller, StringComparison.Ordinal);
        Assert.Contains("_moveAction.ReadValue<Vector2>()", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("Input.GetAxisRaw", controller, StringComparison.Ordinal);
        Assert.Contains("context.painter2D", controller, StringComparison.Ordinal);
        Assert.Contains("DrawCircle", controller, StringComparison.Ordinal);
        Assert.Contains("DrawSegmentedHealth", controller, StringComparison.Ordinal);
        Assert.Contains("DrawDemoBattle", controller, StringComparison.Ordinal);
        Assert.Contains("GenerateArenaVisualContent", controller, StringComparison.Ordinal);
        Assert.Contains("ApplyWorldSnapshot", controller, StringComparison.Ordinal);
        Assert.Contains("new Vector2(bullet.DirectionX, -bullet.DirectionY)", controller, StringComparison.Ordinal);
        Assert.Contains("CameraCenter", controller, StringComparison.Ordinal);
        Assert.Contains("HitEffectDuration", controller, StringComparison.Ordinal);
        Assert.Contains("2166136261", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("new Color(0.2f, 0.9f, 0.3f)", PlayerPaletteSource(controller), StringComparison.Ordinal);
        Assert.Contains("CONNECTING...", controller, StringComparison.Ordinal);
        Assert.Contains("_loginPanel.style.display", controller, StringComparison.Ordinal);

        var scene = AssertPath(plan, "Client/Assets/Scenes/Game.unity").Content;
        Assert.Contains("m_Name: Lakona Arena Game", scene, StringComparison.Ordinal);
        Assert.Contains("m_Name: EventSystem", scene, StringComparison.Ordinal);
        Assert.Contains("01614664b831546d2ae94a42149d80ac", scene, StringComparison.Ordinal);
        Assert.Contains("76c392e42b5098c458856cdf6ecaaaa1", scene, StringComparison.Ordinal);
        Assert.DoesNotContain("4f231c4fb786f3946a6b90b886c48677", scene, StringComparison.Ordinal);
        Assert.Contains(UnityClientAssetTemplates.GameControllerGuid, scene, StringComparison.Ordinal);
        Assert.Contains(UnityClientAssetTemplates.GameUxmlGuid, scene, StringComparison.Ordinal);
        Assert.Contains(UnityClientAssetTemplates.InputActionsGuid, scene, StringComparison.Ordinal);
        Assert.Contains($"_inputActions: {{fileID: -944628639613478452, guid: {UnityClientAssetTemplates.InputActionsGuid}, type: 3}}", scene, StringComparison.Ordinal);
        var inputActions = AssertPath(plan, "Client/Assets/Input/LakonaInputActions.inputactions").Content;
        using (var inputDocument = JsonDocument.Parse(inputActions))
        {
            var playerMap = Assert.Single(inputDocument.RootElement.GetProperty("maps").EnumerateArray());
            Assert.Equal("Player", playerMap.GetProperty("name").GetString());
            Assert.Contains(playerMap.GetProperty("actions").EnumerateArray(), action => action.GetProperty("name").GetString() == "Move");
        }
        AssertPath(plan, "Client/Assets/Input/LakonaInputActions.inputactions.meta");
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
        AssertValidCSharp(code, LanguageVersion.Latest);
        Assert.Contains("public override void _Draw()", code, StringComparison.Ordinal);
        Assert.Contains("DrawCircle", code, StringComparison.Ordinal);
        Assert.Contains("DrawSegmentedHealth", code, StringComparison.Ordinal);
        Assert.Contains("DrawDemoBattle", code, StringComparison.Ordinal);
        Assert.Contains("ApplyWorldSnapshot", code, StringComparison.Ordinal);
        Assert.Contains("HitEffectDuration", code, StringComparison.Ordinal);
        Assert.Contains("Input.IsKeyPressed(Key.W)", code, StringComparison.Ordinal);
        Assert.Contains("while (_client.TryDequeueSnapshot", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshWorldAsync", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GetWorldAsync", code, StringComparison.Ordinal);
        Assert.Contains("new Vector2(bullet.DirectionX, -bullet.DirectionY)", code, StringComparison.Ordinal);
        Assert.Contains("CameraCenter", code, StringComparison.Ordinal);
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
        Assert.DoesNotContain("GetWorldAsync", program, StringComparison.Ordinal);
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
        Assert.Equal("1.14.0", document.RootElement.GetProperty("dependencies").GetProperty("com.unity.inputsystem").GetString());
        Assert.Equal("1.0.0", document.RootElement.GetProperty("dependencies").GetProperty("com.unity.ugui").GetString());
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

    private static void AssertValidCSharp(string source, LanguageVersion languageVersion)
    {
        var diagnostics = CSharpSyntaxTree
            .ParseText(source, new CSharpParseOptions(languageVersion))
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.Empty(diagnostics);
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
