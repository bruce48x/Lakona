using System.Text.Json;
using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Planning;
using Lakona.ProjectSystem.Generation.Rendering.Client;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Lakona.ProjectSystem.Tests.Rendering;

public sealed class ClientRendererTests
{
    private static readonly string[] Unity60PackageIds =
    [
        "com.unity.ai.navigation",
        "com.unity.collab-proxy",
        "com.unity.ide.rider",
        "com.unity.ide.visualstudio",
        "com.unity.inputsystem",
        "com.unity.multiplayer.center",
        "com.unity.render-pipelines.universal",
        "com.unity.test-framework",
        "com.unity.timeline",
        "com.unity.ugui",
        "com.unity.visualscripting",
        "com.unity.modules.accessibility",
        "com.unity.modules.ai",
        "com.unity.modules.androidjni",
        "com.unity.modules.animation",
        "com.unity.modules.assetbundle",
        "com.unity.modules.audio",
        "com.unity.modules.cloth",
        "com.unity.modules.director",
        "com.unity.modules.imageconversion",
        "com.unity.modules.imgui",
        "com.unity.modules.jsonserialize",
        "com.unity.modules.particlesystem",
        "com.unity.modules.physics",
        "com.unity.modules.physics2d",
        "com.unity.modules.screencapture",
        "com.unity.modules.terrain",
        "com.unity.modules.terrainphysics",
        "com.unity.modules.tilemap",
        "com.unity.modules.ui",
        "com.unity.modules.uielements",
        "com.unity.modules.umbra",
        "com.unity.modules.unityanalytics",
        "com.unity.modules.unitywebrequest",
        "com.unity.modules.unitywebrequestassetbundle",
        "com.unity.modules.unitywebrequestaudio",
        "com.unity.modules.unitywebrequesttexture",
        "com.unity.modules.unitywebrequestwww",
        "com.unity.modules.vehicles",
        "com.unity.modules.video",
        "com.unity.modules.vr",
        "com.unity.modules.wind",
        "com.unity.modules.xr"
    ];

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
        Assert.Contains("if (_client.TryConsumeLatestSnapshot", controller, StringComparison.Ordinal);
        var gameClient = AssertPath(plan, "Client/Assets/Scripts/Game/GameClient.cs").Content;
        AssertValidCSharp(gameClient, LanguageVersion.CSharp9);
        Assert.Contains("private WorldSnapshot? _latestSnapshot", gameClient, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref _latestSnapshot, snapshot)", gameClient, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref _latestSnapshot, null)", gameClient, StringComparison.Ordinal);
        Assert.DoesNotContain("ConcurrentQueue<WorldSnapshot>", gameClient, StringComparison.Ordinal);
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
        Assert.DoesNotContain("ext_resource type=\"Script\" uid=", scene, StringComparison.Ordinal);
        Assert.Contains(
            """
            [node name="Action" type="HBoxContainer" parent="Ui/LoginPanel/VBox"]
            custom_minimum_size = Vector2(700, 76)
            layout_mode = 2
            size_flags_horizontal = 4
            """,
            scene,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            [node name="Name" type="LineEdit" parent="Ui/LoginPanel/VBox/Action"]
            custom_minimum_size = Vector2(460, 76)
            """,
            scene,
            StringComparison.Ordinal);

        var code = AssertPath(plan, "Client/Scripts/Game/GameScene.cs").Content;
        AssertValidCSharp(code, LanguageVersion.Latest);
        Assert.Contains("public override void _Draw()", code, StringComparison.Ordinal);
        Assert.Contains("DrawCircle", code, StringComparison.Ordinal);
        Assert.Contains("DrawSegmentedHealth", code, StringComparison.Ordinal);
        Assert.Contains("DrawDemoBattle", code, StringComparison.Ordinal);
        Assert.Contains("ApplyWorldSnapshot", code, StringComparison.Ordinal);
        Assert.Contains("HitEffectDuration", code, StringComparison.Ordinal);
        Assert.Contains("Input.IsKeyPressed(Key.W)", code, StringComparison.Ordinal);
        Assert.Contains("if (_client.TryConsumeLatestSnapshot", code, StringComparison.Ordinal);
        var gameClient = AssertPath(plan, "Client/Scripts/Game/GameClient.cs").Content;
        AssertValidCSharp(gameClient, LanguageVersion.Latest);
        Assert.Contains("private WorldSnapshot? _latestSnapshot", gameClient, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref _latestSnapshot, snapshot)", gameClient, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref _latestSnapshot, null)", gameClient, StringComparison.Ordinal);
        Assert.DoesNotContain("ConcurrentQueue<WorldSnapshot>", gameClient, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshWorldAsync", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GetWorldAsync", code, StringComparison.Ordinal);
        Assert.Contains("new Vector2(bullet.DirectionX, -bullet.DirectionY)", code, StringComparison.Ordinal);
        Assert.Contains("CameraCenter", code, StringComparison.Ordinal);
        Assert.Contains("_loginPending = true", code, StringComparison.Ordinal);
        Assert.Contains("LAKONA_GODOT_SMOKE", code, StringComparison.Ordinal);
        Assert.Contains("RunHeadlessSmokeAsync", code, StringComparison.Ordinal);
        Assert.Contains("await client.LoginAsync(name)", code, StringComparison.Ordinal);
        Assert.Contains("Arena smoke ok:", code, StringComparison.Ordinal);
        Assert.Contains("GetTree().Quit(0)", code, StringComparison.Ordinal);
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
        var project = AssertPath(plan, "Client/Client.csproj").Content;
        Assert.Contains("<LakonaRpcGenerateClient>true</LakonaRpcGenerateClient>", project, StringComparison.Ordinal);
        Assert.Contains("<LakonaRpcGeneratedNamespace>Client.Generated</LakonaRpcGeneratedNamespace>", project, StringComparison.Ordinal);
        Assert.Contains("<LakonaGameGenerateClient>true</LakonaGameGenerateClient>", project, StringComparison.Ordinal);
        Assert.DoesNotContain("<CompilerVisibleProperty", project, StringComparison.Ordinal);
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
        var project = AssertPath(plan, "Client/Client.csproj").Content;
        Assert.Contains("<LakonaRpcGenerateClient>true</LakonaRpcGenerateClient>", project, StringComparison.Ordinal);
        Assert.Contains("<LakonaRpcGeneratedNamespace>Client.Generated</LakonaRpcGeneratedNamespace>", project, StringComparison.Ordinal);
        Assert.Contains("<LakonaGameGenerateClient>true</LakonaGameGenerateClient>", project, StringComparison.Ordinal);
        Assert.DoesNotContain("<CompilerVisibleProperty", project, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Unity", "OpenUpm")]
    [InlineData("Unity", "Embedded")]
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
    public void UnityClientRenderer_ManifestIncludesUnity2022DefaultPackages()
    {
        var plan = Render(new UnityClientRenderer(), Spec(ClientEngine.Unity, TransportKind.Kcp, SerializerKind.MemoryPack));
        using var document = JsonDocument.Parse(AssertPath(plan, "Client/Packages/manifest.json").Content);
        var dependencies = document.RootElement.GetProperty("dependencies");
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["com.unity.collab-proxy"] = "2.12.4",
            ["com.unity.feature.development"] = "1.0.1",
            ["com.unity.textmeshpro"] = "3.0.7",
            ["com.unity.timeline"] = "1.7.7",
            ["com.unity.ugui"] = "1.0.0",
            ["com.unity.visualscripting"] = "1.9.4",
            ["com.unity.modules.ai"] = "1.0.0",
            ["com.unity.modules.androidjni"] = "1.0.0",
            ["com.unity.modules.animation"] = "1.0.0",
            ["com.unity.modules.assetbundle"] = "1.0.0",
            ["com.unity.modules.audio"] = "1.0.0",
            ["com.unity.modules.cloth"] = "1.0.0",
            ["com.unity.modules.director"] = "1.0.0",
            ["com.unity.modules.imageconversion"] = "1.0.0",
            ["com.unity.modules.imgui"] = "1.0.0",
            ["com.unity.modules.jsonserialize"] = "1.0.0",
            ["com.unity.modules.particlesystem"] = "1.0.0",
            ["com.unity.modules.physics"] = "1.0.0",
            ["com.unity.modules.physics2d"] = "1.0.0",
            ["com.unity.modules.screencapture"] = "1.0.0",
            ["com.unity.modules.terrain"] = "1.0.0",
            ["com.unity.modules.terrainphysics"] = "1.0.0",
            ["com.unity.modules.tilemap"] = "1.0.0",
            ["com.unity.modules.ui"] = "1.0.0",
            ["com.unity.modules.uielements"] = "1.0.0",
            ["com.unity.modules.umbra"] = "1.0.0",
            ["com.unity.modules.unityanalytics"] = "1.0.0",
            ["com.unity.modules.unitywebrequest"] = "1.0.0",
            ["com.unity.modules.unitywebrequestassetbundle"] = "1.0.0",
            ["com.unity.modules.unitywebrequestaudio"] = "1.0.0",
            ["com.unity.modules.unitywebrequesttexture"] = "1.0.0",
            ["com.unity.modules.unitywebrequestwww"] = "1.0.0",
            ["com.unity.modules.vehicles"] = "1.0.0",
            ["com.unity.modules.video"] = "1.0.0",
            ["com.unity.modules.vr"] = "1.0.0",
            ["com.unity.modules.wind"] = "1.0.0",
            ["com.unity.modules.xr"] = "1.0.0"
        };

        foreach (var (packageId, version) in expected)
        {
            Assert.Equal(version, dependencies.GetProperty(packageId).GetString());
        }
    }

    [Fact]
    public void UnityClientRenderer_ManifestIncludesTuanjieDefaultPackages()
    {
        var plan = Render(new UnityClientRenderer(), Spec(ClientEngine.Tuanjie, TransportKind.Kcp, SerializerKind.MemoryPack, NuGetForUnitySource.Embedded));
        using var document = JsonDocument.Parse(AssertPath(plan, "Client/Packages/manifest.json").Content);
        var dependencies = document.RootElement.GetProperty("dependencies");
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cn.tuanjie.codely.bridge"] = "1.0.69",
            ["com.unity.collab-proxy"] = "2.5.1",
            ["com.unity.feature.development"] = "1.0.1",
            ["com.unity.textmeshpro"] = "3.0.7",
            ["com.unity.timeline"] = "1.7.7",
            ["com.unity.ugui"] = "1.0.0",
            ["com.unity.visualscripting"] = "1.9.4",
            ["com.unity.modules.ai"] = "1.0.0",
            ["com.unity.modules.androidjni"] = "1.0.0",
            ["com.unity.modules.animation"] = "1.0.0",
            ["com.unity.modules.assetbundle"] = "1.0.0",
            ["com.unity.modules.audio"] = "1.0.0",
            ["com.unity.modules.cloth"] = "1.0.0",
            ["com.unity.modules.director"] = "1.0.0",
            ["com.unity.modules.imageconversion"] = "1.0.0",
            ["com.unity.modules.imgui"] = "1.0.0",
            ["com.unity.modules.infinity"] = "1.0.0",
            ["com.unity.modules.jsonserialize"] = "1.0.0",
            ["com.unity.modules.particlesystem"] = "1.0.0",
            ["com.unity.modules.physics"] = "1.0.0",
            ["com.unity.modules.physics2d"] = "1.0.0",
            ["com.unity.modules.screencapture"] = "1.0.0",
            ["com.unity.modules.terrain"] = "1.0.0",
            ["com.unity.modules.terrainphysics"] = "1.0.0",
            ["com.unity.modules.tilemap"] = "1.0.0",
            ["com.unity.modules.ui"] = "1.0.0",
            ["com.unity.modules.uielements"] = "1.0.0",
            ["com.unity.modules.unityanalytics"] = "1.0.0",
            ["com.unity.modules.unitywebrequest"] = "1.0.0",
            ["com.unity.modules.unitywebrequestassetbundle"] = "1.0.0",
            ["com.unity.modules.unitywebrequestaudio"] = "1.0.0",
            ["com.unity.modules.unitywebrequesttexture"] = "1.0.0",
            ["com.unity.modules.unitywebrequestwww"] = "1.0.0",
            ["com.unity.modules.vehicles"] = "1.0.0",
            ["com.unity.modules.video"] = "1.0.0",
            ["com.unity.modules.vr"] = "1.0.0",
            ["com.unity.modules.wind"] = "1.0.0",
            ["com.unity.modules.xr"] = "1.0.0"
        };

        foreach (var (packageId, version) in expected)
        {
            Assert.Equal(version, dependencies.GetProperty(packageId).GetString());
        }

        Assert.Equal(expected.Count + 2, dependencies.EnumerateObject().Count());
        Assert.False(dependencies.TryGetProperty("com.unity.modules.umbra", out _));
    }

    [Theory]
    [InlineData(
        "Unity60",
        "6000.0.52f1",
        "9e4086222921",
        45,
        "2.0.8",
        "2.13.3",
        "3.0.36",
        "2.0.23",
        "1.14.0",
        "1.0.0",
        "17.0.4",
        "1.5.1",
        "1.8.7",
        "2.0.0",
        "1.9.7")]
    [InlineData(
        "Unity63",
        "6000.3.3f1",
        "ef04196de0d6",
        47,
        "2.0.9",
        "2.10.2",
        "3.0.38",
        "2.0.25",
        "1.17.0",
        "1.0.1",
        "17.3.0",
        "1.6.0",
        "1.8.10",
        "2.0.0",
        "1.9.9")]
    public void UnityClientRenderer_UsesSelectedUnity6PackageBaseline(
        string versionName,
        string editorVersion,
        string revision,
        int expectedGeneratedDependencyCount,
        string navigationVersion,
        string collabVersion,
        string riderVersion,
        string visualStudioVersion,
        string inputSystemVersion,
        string multiplayerCenterVersion,
        string universalRenderPipelineVersion,
        string testFrameworkVersion,
        string timelineVersion,
        string uguiVersion,
        string visualScriptingVersion)
    {
        var version = Enum.Parse<ClientEngineVersion>(versionName);
        var plan = Render(
            new UnityClientRenderer(),
            Spec(
                ClientEngine.Unity,
                TransportKind.Kcp,
                SerializerKind.MemoryPack,
                version: version));
        using var document = JsonDocument.Parse(AssertPath(plan, "Client/Packages/manifest.json").Content);
        var dependencies = document.RootElement.GetProperty("dependencies");

        Assert.Equal(expectedGeneratedDependencyCount, dependencies.EnumerateObject().Count());
        Assert.Equal(navigationVersion, dependencies.GetProperty("com.unity.ai.navigation").GetString());
        Assert.Equal(collabVersion, dependencies.GetProperty("com.unity.collab-proxy").GetString());
        Assert.Equal(riderVersion, dependencies.GetProperty("com.unity.ide.rider").GetString());
        Assert.Equal(visualStudioVersion, dependencies.GetProperty("com.unity.ide.visualstudio").GetString());
        Assert.Equal(inputSystemVersion, dependencies.GetProperty("com.unity.inputsystem").GetString());
        Assert.Equal(multiplayerCenterVersion, dependencies.GetProperty("com.unity.multiplayer.center").GetString());
        Assert.Equal(universalRenderPipelineVersion, dependencies.GetProperty("com.unity.render-pipelines.universal").GetString());
        Assert.Equal(testFrameworkVersion, dependencies.GetProperty("com.unity.test-framework").GetString());
        Assert.Equal(timelineVersion, dependencies.GetProperty("com.unity.timeline").GetString());
        Assert.Equal(uguiVersion, dependencies.GetProperty("com.unity.ugui").GetString());
        Assert.Equal(visualScriptingVersion, dependencies.GetProperty("com.unity.visualscripting").GetString());
        Assert.Equal("1.0.0", dependencies.GetProperty("com.unity.modules.accessibility").GetString());
        Assert.Equal("1.0.0", dependencies.GetProperty("com.unity.modules.uielements").GetString());

        var expectedPackageIds = version == ClientEngineVersion.Unity63
            ? Unity60PackageIds
                .Append("com.unity.modules.adaptiveperformance")
                .Append("com.unity.modules.vectorgraphics")
                .Order(StringComparer.Ordinal)
                .ToArray()
            : Unity60PackageIds.Order(StringComparer.Ordinal).ToArray();
        var actualPackageIds = dependencies
            .EnumerateObject()
            .Select(static dependency => dependency.Name)
            .Where(static packageId =>
                packageId != "com.github-glitchenzo.nugetforunity" &&
                !packageId.StartsWith("com.lakona.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedPackageIds, actualPackageIds);

        if (version == ClientEngineVersion.Unity63)
        {
            Assert.Equal("1.0.0", dependencies.GetProperty("com.unity.modules.adaptiveperformance").GetString());
            Assert.Equal("1.0.0", dependencies.GetProperty("com.unity.modules.vectorgraphics").GetString());
        }
        else
        {
            Assert.False(dependencies.TryGetProperty("com.unity.modules.adaptiveperformance", out _));
            Assert.False(dependencies.TryGetProperty("com.unity.modules.vectorgraphics", out _));
        }

        var projectVersion = AssertPath(plan, "Client/ProjectSettings/ProjectVersion.txt").Content;
        Assert.Contains($"m_EditorVersion: {editorVersion}", projectVersion, StringComparison.Ordinal);
        Assert.Contains(
            $"m_EditorVersionWithRevision: {editorVersion} ({revision})",
            projectVersion,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UnityClientRenderer_DefaultsToUnity2022ReferenceVersion()
    {
        var plan = Render(
            new UnityClientRenderer(),
            Spec(ClientEngine.Unity, TransportKind.Kcp, SerializerKind.MemoryPack));

        var projectVersion = AssertPath(plan, "Client/ProjectSettings/ProjectVersion.txt").Content;
        Assert.Equal(
            "m_EditorVersion: 2022.3.62f3c1\n" +
            "m_EditorVersionWithRevision: 2022.3.62f3c1 (1623fc0bbb97)",
            projectVersion);
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
        NuGetForUnitySource source = NuGetForUnitySource.OpenUpm,
        ClientEngineVersion? version = null) =>
        new ProjectSpecTestFactory().Create(new ProjectSpecTestOptions(
            "MyGame",
            ".",
            engine,
            transport,
            serializer,
            source,
            DeploymentProfile.None,
            ProjectSpecTestOptionPresence.NuGetForUnitySource,
            version));

    private static GeneratedFile AssertPath(GenerationPlan plan, string relativePath) =>
        Assert.Single(plan.Files, file => file.RelativePath == relativePath);
}
