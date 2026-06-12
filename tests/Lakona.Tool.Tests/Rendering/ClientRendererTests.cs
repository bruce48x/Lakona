using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Client;
using Xunit;

namespace Lakona.Tool.Tests.Rendering;

public sealed class ClientRendererTests
{
    [Fact]
    public void UnityClientRenderer_EmitsUnityFilesAndNoGodotFiles()
    {
        var plan = Render(new UnityClientRenderer(), Spec(ClientEngine.Unity));

        Assert.Contains(plan.Files, file => file.RelativePath == "Client/Packages/manifest.json");
        Assert.Contains(plan.Files, file => file.RelativePath == "Client/Assets/packages.config");
        Assert.Contains(plan.Files, file => file.RelativePath == "Client/Assets/NuGet.config");
        Assert.Contains(plan.Files, file => file.RelativePath == "Client/ProjectSettings/ProjectVersion.txt");
        Assert.DoesNotContain(plan.Files, file => file.RelativePath.EndsWith(".tscn", StringComparison.Ordinal));
    }

    [Fact]
    public void UnityClientRenderer_NuGetConfig_EmitsNuGetForUnityRestoreSettings()
    {
        var plan = Render(new UnityClientRenderer(), Spec(ClientEngine.Unity));
        var config = AssertPath(plan, "Client/Assets/NuGet.config").Content;

        Assert.Contains("<disabledPackageSources />", config, StringComparison.Ordinal);
        Assert.Contains("<activePackageSource>", config, StringComparison.Ordinal);
        Assert.Contains("<add key=\"All\" value=\"(Aggregate source)\" />", config, StringComparison.Ordinal);
        Assert.Contains("<add key=\"packageInstallLocation\" value=\"CustomWithinAssets\" />", config, StringComparison.Ordinal);
        Assert.Contains("<add key=\"repositoryPath\" value=\"./Packages\" />", config, StringComparison.Ordinal);
        Assert.Contains("<add key=\"PackagesConfigDirectoryPath\" value=\".\" />", config, StringComparison.Ordinal);
        Assert.Contains("<add key=\"slimRestore\" value=\"true\" />", config, StringComparison.Ordinal);
        Assert.Contains("<add key=\"PreferNetStandardOverNetFramework\" value=\"true\" />", config, StringComparison.Ordinal);
    }

    [Fact]
    public void UnityClientRenderer_EmitsPlayableChatClientSlice()
    {
        var plan = Render(new UnityClientRenderer(), Spec(ClientEngine.Unity));

        var manifest = AssertPath(plan, "Client/Packages/manifest.json").Content;
        Assert.Contains("\"com.unity.modules.uielements\": \"1.0.0\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"com.unity.modules.ui\": \"1.0.0\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"com.unity.modules.audio\": \"1.0.0\"", manifest, StringComparison.Ordinal);

        var rpcMarker = AssertPath(plan, "Client/Assets/Scripts/Rpc/LakonaRpcGeneration.cs").Content;
        Assert.Contains("[assembly: LakonaRpcGenerateClient(\"Rpc.Generated\")]", rpcMarker, StringComparison.Ordinal);

        var loginClient = AssertPath(plan, "Client/Assets/Scripts/Login/LoginClient.cs").Content;
        Assert.Contains("public sealed class LoginClient : ILoginCallback, IChatCallback, IAsyncDisposable", loginClient, StringComparison.Ordinal);
        Assert.Contains("callbacks.Add((ILoginCallback)this);", loginClient, StringComparison.Ordinal);
        Assert.Contains("callbacks.Add((IChatCallback)this);", loginClient, StringComparison.Ordinal);
        Assert.Contains("_loginService = _rpcClient.Api.Shared.Login;", loginClient, StringComparison.Ordinal);
        Assert.Contains("public async Task<LoginReply> LoginAsync(string playerName)", loginClient, StringComparison.Ordinal);
        Assert.DoesNotContain("public sealed class LoginClient\r\n    {\r\n    }", loginClient, StringComparison.Ordinal);

        var chatClient = AssertPath(plan, "Client/Assets/Scripts/Chat/ChatClient.cs").Content;
        Assert.Contains("private readonly IChatService _chatService;", chatClient, StringComparison.Ordinal);
        Assert.Contains("_chatService = loginClient.RpcClient.Api.Shared.Chat;", chatClient, StringComparison.Ordinal);
        Assert.Contains("public async Task BindAsync()", chatClient, StringComparison.Ordinal);
        Assert.Contains("public async Task SendAsync(string text)", chatClient, StringComparison.Ordinal);

        var loginUi = AssertPath(plan, "Client/Assets/Scripts/Login/LoginUI.cs").Content;
        Assert.Contains("using Lakona.Rpc.Transport.Kcp;", loginUi, StringComparison.Ordinal);
        Assert.Contains("using Lakona.Rpc.Serializer.MemoryPack;", loginUi, StringComparison.Ordinal);
        Assert.Contains("new KcpTransport(_serverHost, _serverPort)", loginUi, StringComparison.Ordinal);
        Assert.Contains("new MemoryPackRpcSerializer()", loginUi, StringComparison.Ordinal);
        Assert.Contains("ChatSession.LoginClient = client;", loginUi, StringComparison.Ordinal);

        var chatUi = AssertPath(plan, "Client/Assets/Scripts/Chat/ChatUI.cs").Content;
        Assert.Contains("private LoginClient? _loginClient;", chatUi, StringComparison.Ordinal);
        Assert.Contains("_client = new ChatClient(loginClient);", chatUi, StringComparison.Ordinal);
        Assert.Contains("await _client.BindAsync();", chatUi, StringComparison.Ordinal);
        Assert.Contains("await _client.SendAsync(text);", chatUi, StringComparison.Ordinal);

        var chatUss = AssertPath(plan, "Client/Assets/UI/ChatScene.uss").Content;
        Assert.Contains("flex-shrink: 0;", ExtractCssRule(chatUss, ".message-label"), StringComparison.Ordinal);
        Assert.Contains("flex-grow: 1;", ExtractCssRule(chatUss, ".chat-input"), StringComparison.Ordinal);
        Assert.Contains("flex-shrink: 1;", ExtractCssRule(chatUss, ".chat-input"), StringComparison.Ordinal);
        Assert.Contains("min-width: 0;", ExtractCssRule(chatUss, ".chat-input"), StringComparison.Ordinal);
        Assert.Contains("width: 96px;", ExtractCssRule(chatUss, ".send-button"), StringComparison.Ordinal);
        Assert.Contains("min-width: 96px;", ExtractCssRule(chatUss, ".send-button"), StringComparison.Ordinal);
        Assert.Contains("flex-shrink: 0;", ExtractCssRule(chatUss, ".send-button"), StringComparison.Ordinal);

        AssertPath(plan, "Client/Assets/Scripts/Chat/ChatSession.cs");
        AssertPath(plan, "Client/Assets/Scripts/Rpc/LakonaRpcGeneration.cs.meta");
        AssertPath(plan, "Client/Assets/Scripts/Login/LoginUI.cs.meta");
        AssertPath(plan, "Client/Assets/Scripts/Chat/ChatUI.cs.meta");
        AssertPath(plan, "Client/Assets/UI/LoginScene.uxml");
        AssertPath(plan, "Client/Assets/UI/LoginScene.uss");
        AssertPath(plan, "Client/Assets/UI/ChatScene.uxml");
        AssertPath(plan, "Client/Assets/UI/ChatScene.uss");
        AssertPath(plan, "Client/Assets/UI/LakonaGameChatPanelSettings.asset");
        AssertPath(plan, "Client/Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");
        var loginScene = AssertPath(plan, "Client/Assets/Scenes/LoginScene.unity").Content;
        AssertUnitySceneHasMainCamera(loginScene);
        var chatScene = AssertPath(plan, "Client/Assets/Scenes/ChatScene.unity").Content;
        AssertUnitySceneHasMainCamera(chatScene);
        AssertPath(plan, "Client/Assets/Editor/LakonaGameNuGetPackageImportGuard.cs");
    }

    [Theory]
    [InlineData("Tcp", "Json", "using Lakona.Rpc.Transport.Tcp;", "using Lakona.Rpc.Serializer.Json;", "new TcpTransport(_serverHost, _serverPort)", "new JsonRpcSerializer()")]
    [InlineData("WebSocket", "Json", "using Lakona.Rpc.Transport.WebSocket;", "using Lakona.Rpc.Serializer.Json;", "new WsTransport($\"ws://{_serverHost}:{_serverPort}{NormalizePath(_serverPath)}\")", "new JsonRpcSerializer()")]
    [InlineData("Kcp", "MemoryPack", "using Lakona.Rpc.Transport.Kcp;", "using Lakona.Rpc.Serializer.MemoryPack;", "new KcpTransport(_serverHost, _serverPort)", "new MemoryPackRpcSerializer()")]
    public void UnityClientRenderer_LoginUiUsesSelectedTransportAndSerializer(
        string transportName,
        string serializerName,
        string transportUsing,
        string serializerUsing,
        string transportExpression,
        string serializerExpression)
    {
        var transport = Enum.Parse<TransportKind>(transportName);
        var serializer = Enum.Parse<SerializerKind>(serializerName);
        var plan = Render(new UnityClientRenderer(), Spec(ClientEngine.Unity, serializer: serializer, transport: transport));
        var loginUi = AssertPath(plan, "Client/Assets/Scripts/Login/LoginUI.cs").Content;

        Assert.Contains(transportUsing, loginUi, StringComparison.Ordinal);
        Assert.Contains(serializerUsing, loginUi, StringComparison.Ordinal);
        Assert.Contains(transportExpression, loginUi, StringComparison.Ordinal);
        Assert.Contains(serializerExpression, loginUi, StringComparison.Ordinal);
    }

    [Fact]
    public void GodotClientRenderer_EmitsGodotFilesAndNoUnityFiles()
    {
        var plan = Render(new GodotClientRenderer(), Spec(ClientEngine.Godot));

        Assert.Contains(plan.Files, file => file.RelativePath == "Client/project.godot");
        Assert.Contains(plan.Files, file => file.RelativePath == "Client/Client.csproj");
        Assert.Contains(plan.Files, file => file.RelativePath == "Client/Login.tscn");
        Assert.Contains(plan.Files, file => file.RelativePath == "Client/Chat.tscn");
        Assert.Contains(plan.Files, file => file.RelativePath == "Client/Scripts/Login/LoginClient.cs");
        Assert.Contains(plan.Files, file => file.RelativePath == "Client/Scripts/Chat/ChatClient.cs");
        Assert.Contains(plan.Files, file => file.RelativePath == "Client/Scripts/Chat/ChatSession.cs");
        Assert.Contains(plan.Files, file => file.RelativePath == "Client/Theme/LakonaTheme.tres");
        Assert.DoesNotContain(plan.Files, file => file.RelativePath == "Client/Scripts/Theme/LakonaTheme.cs");
        Assert.DoesNotContain(plan.Files, file => file.RelativePath.StartsWith("Client/Assets/", StringComparison.Ordinal));
    }

    [Fact]
    public void GodotClientRenderer_EmitsPlayableChatClientSlice()
    {
        var plan = Render(new GodotClientRenderer(), Spec(ClientEngine.Godot, serializer: SerializerKind.Json, transport: TransportKind.WebSocket));

        var project = AssertPath(plan, "Client/Client.csproj").Content;
        Assert.Contains("<Nullable>enable</Nullable>", project, StringComparison.Ordinal);
        Assert.Contains("<ImplicitUsings>enable</ImplicitUsings>", project, StringComparison.Ordinal);
        Assert.Contains("<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>", project, StringComparison.Ordinal);
        Assert.Contains("<LakonaRpcGeneratedNamespace>Rpc.Generated</LakonaRpcGeneratedNamespace>", project, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Lakona.Rpc.Serializer.Json\"", project, StringComparison.Ordinal);

        var projectGodot = AssertPath(plan, "Client/project.godot").Content;
        Assert.Contains("config/features=PackedStringArray(\"4.6\", \"C#\")", projectGodot, StringComparison.Ordinal);
        Assert.Contains("[autoload]", projectGodot, StringComparison.Ordinal);
        Assert.Contains("ChatSession=\"*res://Scripts/Chat/ChatSession.cs\"", projectGodot, StringComparison.Ordinal);
        Assert.Contains("[dotnet]", projectGodot, StringComparison.Ordinal);
        Assert.Contains("project/assembly_name=\"Client\"", projectGodot, StringComparison.Ordinal);

        var loginClient = AssertPath(plan, "Client/Scripts/Login/LoginClient.cs").Content;
        Assert.Contains("using Rpc.Generated;", loginClient, StringComparison.Ordinal);
        Assert.Contains("public sealed class LoginClient : ILoginCallback, IChatCallback, IAsyncDisposable", loginClient, StringComparison.Ordinal);
        Assert.Contains("callbacks.Add((ILoginCallback)this);", loginClient, StringComparison.Ordinal);
        Assert.Contains("callbacks.Add((IChatCallback)this);", loginClient, StringComparison.Ordinal);
        Assert.Contains("_loginService = _rpcClient.Api.Shared.Login;", loginClient, StringComparison.Ordinal);

        var chatClient = AssertPath(plan, "Client/Scripts/Chat/ChatClient.cs").Content;
        Assert.Contains("private readonly IChatService _chatService;", chatClient, StringComparison.Ordinal);
        Assert.Contains("_chatService = loginClient.RpcClient.Api.Shared.Chat;", chatClient, StringComparison.Ordinal);
        Assert.Contains("public async Task BindAsync()", chatClient, StringComparison.Ordinal);
        Assert.Contains("public async Task SendAsync(string text)", chatClient, StringComparison.Ordinal);

        var session = AssertPath(plan, "Client/Scripts/Chat/ChatSession.cs").Content;
        Assert.Contains("public partial class ChatSession : Node", session, StringComparison.Ordinal);
        Assert.Contains("public LoginClient? LoginClient { get; set; }", session, StringComparison.Ordinal);
        Assert.Contains("public LoginReply? LoginReply { get; set; }", session, StringComparison.Ordinal);

        var loginScene = AssertPath(plan, "Client/Scripts/Login/LoginScene.cs").Content;
        Assert.DoesNotContain("private void BuildUi()", loginScene, StringComparison.Ordinal);
        Assert.DoesNotContain("using Client.Theme;", loginScene, StringComparison.Ordinal);
        Assert.DoesNotContain("new ColorRect", loginScene, StringComparison.Ordinal);
        Assert.Contains("GetNode<LineEdit>(\"%NameField\")", loginScene, StringComparison.Ordinal);
        Assert.Contains("GetNode<Button>(\"%ConnectButton\")", loginScene, StringComparison.Ordinal);
        Assert.Contains("GetNode<Label>(\"%StatusLabel\")", loginScene, StringComparison.Ordinal);
        Assert.Contains("new WsTransport($\"ws://{_serverHost}:{_serverPort}{NormalizePath(_serverPath)}\")", loginScene, StringComparison.Ordinal);
        Assert.Contains("new JsonRpcSerializer()", loginScene, StringComparison.Ordinal);
        Assert.Contains("var session = GetNode<ChatSession>(\"/root/ChatSession\");", loginScene, StringComparison.Ordinal);
        Assert.Contains("GetTree().ChangeSceneToFile(\"res://Chat.tscn\");", loginScene, StringComparison.Ordinal);

        var chatScene = AssertPath(plan, "Client/Scripts/Chat/ChatScene.cs").Content;
        Assert.DoesNotContain("private void BuildUi()", chatScene, StringComparison.Ordinal);
        Assert.DoesNotContain("using Client.Theme;", chatScene, StringComparison.Ordinal);
        Assert.DoesNotContain("new PanelContainer", chatScene, StringComparison.Ordinal);
        Assert.Contains("GetNode<LineEdit>(\"%MessageField\")", chatScene, StringComparison.Ordinal);
        Assert.Contains("GetNode<Button>(\"%SendButton\")", chatScene, StringComparison.Ordinal);
        Assert.Contains("GetNode<RichTextLabel>(\"%MessageLog\")", chatScene, StringComparison.Ordinal);
        Assert.Contains("GetNode<Label>(\"%OnlineCount\")", chatScene, StringComparison.Ordinal);
        Assert.Contains("_client = new ChatClient(loginClient);", chatScene, StringComparison.Ordinal);
        Assert.Contains("if (_client == null)", chatScene, StringComparison.Ordinal);
        Assert.Contains("await _client.BindAsync();", chatScene, StringComparison.Ordinal);
        Assert.Contains("await _client.SendAsync(text);", chatScene, StringComparison.Ordinal);
        Assert.Contains("System.Environment.NewLine", chatScene, StringComparison.Ordinal);

        var theme = AssertPath(plan, "Client/Theme/LakonaTheme.tres").Content;
        Assert.Contains("[gd_resource type=\"Theme\" load_steps=8 format=3]", theme, StringComparison.Ordinal);
        Assert.Contains("Button/styles/disabled = SubResource(\"3\")", theme, StringComparison.Ordinal);
        Assert.Contains("TitleLabel/type = \"Label\"", theme, StringComparison.Ordinal);
        Assert.Contains("NameLabel/type = \"Label\"", theme, StringComparison.Ordinal);
        Assert.Contains("ChatHeader/type = \"PanelContainer\"", theme, StringComparison.Ordinal);
        Assert.Contains("ChatFooter/type = \"PanelContainer\"", theme, StringComparison.Ordinal);

        var loginTscn = AssertPath(plan, "Client/Login.tscn").Content;
        Assert.Contains("[gd_scene load_steps=3 format=3]", loginTscn, StringComparison.Ordinal);
        Assert.Contains("[ext_resource type=\"Theme\" path=\"res://Theme/LakonaTheme.tres\" id=\"2\"]", loginTscn, StringComparison.Ordinal);
        Assert.Contains("[node name=\"LoginScene\" type=\"Control\"]", loginTscn, StringComparison.Ordinal);
        Assert.Contains("theme = ExtResource(\"2\")", loginTscn, StringComparison.Ordinal);
        Assert.Contains("[node name=\"LoginPanel\" type=\"PanelContainer\" parent=\"Center\"]", loginTscn, StringComparison.Ordinal);
        Assert.Contains("theme_type_variation = &\"LoginPanel\"", loginTscn, StringComparison.Ordinal);
        Assert.DoesNotContain("theme_type_variation = LoginPanel", loginTscn, StringComparison.Ordinal);
        Assert.Contains("[node name=\"NameField\" type=\"LineEdit\" parent=\"Center/LoginPanel/PanelContent\"]", loginTscn, StringComparison.Ordinal);
        Assert.Contains("[node name=\"ConnectButton\" type=\"Button\" parent=\"Center/LoginPanel/PanelContent\"]", loginTscn, StringComparison.Ordinal);
        Assert.Contains("[node name=\"StatusLabel\" type=\"Label\" parent=\"Center/LoginPanel/PanelContent\"]", loginTscn, StringComparison.Ordinal);
        Assert.Equal(5, CountOccurrences(loginTscn, "theme_type_variation = &\""));
        Assert.Equal(3, CountOccurrences(loginTscn, "unique_name_in_owner = true"));

        var chatTscn = AssertPath(plan, "Client/Chat.tscn").Content;
        Assert.Contains("[gd_scene load_steps=3 format=3]", chatTscn, StringComparison.Ordinal);
        Assert.Contains("[ext_resource type=\"Theme\" path=\"res://Theme/LakonaTheme.tres\" id=\"2\"]", chatTscn, StringComparison.Ordinal);
        Assert.Contains("[node name=\"ChatScene\" type=\"Control\"]", chatTscn, StringComparison.Ordinal);
        Assert.Contains("theme = ExtResource(\"2\")", chatTscn, StringComparison.Ordinal);
        Assert.Contains("[node name=\"Header\" type=\"PanelContainer\" parent=\"Layout/ChatLayout\"]", chatTscn, StringComparison.Ordinal);
        Assert.Contains("[node name=\"MessageLog\" type=\"RichTextLabel\" parent=\"Layout/ChatLayout\"]", chatTscn, StringComparison.Ordinal);
        Assert.Contains("[node name=\"MessageField\" type=\"LineEdit\" parent=\"Layout/ChatLayout/Footer/SendRow\"]", chatTscn, StringComparison.Ordinal);
        Assert.Contains("[node name=\"SendButton\" type=\"Button\" parent=\"Layout/ChatLayout/Footer/SendRow\"]", chatTscn, StringComparison.Ordinal);
        Assert.Contains("theme_type_variation = &\"ChatHeader\"", chatTscn, StringComparison.Ordinal);
        Assert.Contains("theme_type_variation = &\"ChatFooter\"", chatTscn, StringComparison.Ordinal);
        Assert.DoesNotContain("theme_type_variation = ChatHeader", chatTscn, StringComparison.Ordinal);
        Assert.DoesNotContain("theme_type_variation = ChatFooter", chatTscn, StringComparison.Ordinal);
        Assert.Equal(9, CountOccurrences(chatTscn, "theme_type_variation = &\""));
        Assert.Equal(4, CountOccurrences(chatTscn, "unique_name_in_owner = true"));

        AssertPath(plan, "Client/Scripts/Login/LoginClient.cs.uid");
        AssertPath(plan, "Client/Scripts/Chat/ChatClient.cs.uid");
        AssertPath(plan, "Client/Scripts/Chat/ChatSession.cs.uid");
        Assert.DoesNotContain(plan.Files, file => file.RelativePath == "Client/Scripts/Theme/LakonaTheme.cs.uid");
    }

    [Theory]
    [InlineData("Tcp", "Json", "using Lakona.Rpc.Transport.Tcp;", "using Lakona.Rpc.Serializer.Json;", "new TcpTransport(_serverHost, _serverPort)", "new JsonRpcSerializer()")]
    [InlineData("WebSocket", "Json", "using Lakona.Rpc.Transport.WebSocket;", "using Lakona.Rpc.Serializer.Json;", "new WsTransport($\"ws://{_serverHost}:{_serverPort}{NormalizePath(_serverPath)}\")", "new JsonRpcSerializer()")]
    [InlineData("Kcp", "MemoryPack", "using Lakona.Rpc.Transport.Kcp;", "using Lakona.Rpc.Serializer.MemoryPack;", "new KcpTransport(_serverHost, _serverPort)", "new MemoryPackRpcSerializer()")]
    public void GodotClientRenderer_LoginSceneUsesSelectedTransportAndSerializer(
        string transportName,
        string serializerName,
        string transportUsing,
        string serializerUsing,
        string transportExpression,
        string serializerExpression)
    {
        var transport = Enum.Parse<TransportKind>(transportName);
        var serializer = Enum.Parse<SerializerKind>(serializerName);
        var plan = Render(new GodotClientRenderer(), Spec(ClientEngine.Godot, serializer: serializer, transport: transport));
        var loginScene = AssertPath(plan, "Client/Scripts/Login/LoginScene.cs").Content;

        Assert.Contains(transportUsing, loginScene, StringComparison.Ordinal);
        Assert.Contains(serializerUsing, loginScene, StringComparison.Ordinal);
        Assert.Contains(transportExpression, loginScene, StringComparison.Ordinal);
        Assert.Contains(serializerExpression, loginScene, StringComparison.Ordinal);
    }

    [Fact]
    public void UnityClientRenderer_OpenUpmManifest_IncludesNuGetForUnityRegistry()
    {
        var plan = Render(new UnityClientRenderer(), Spec(ClientEngine.Unity, NuGetForUnitySource.OpenUpm));
        var manifest = Assert.Single(plan.Files, file => file.RelativePath == "Client/Packages/manifest.json").Content;

        Assert.Contains("\"com.github-glitchenzo.nugetforunity\": \"4.5.0\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"com.unity.modules.uielements\": \"1.0.0\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"com.unity.modules.audio\": \"1.0.0\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"com.unity.modules.physics\": \"1.0.0\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"com.unity.modules.physics2d\": \"1.0.0\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"scopedRegistries\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"OpenUPM\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"url\": \"https://package.openupm.com\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"com.github-glitchenzo.nugetforunity\"", manifest, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UnityCn")]
    [InlineData("Tuanjie")]
    public void UnityClientRenderer_EmbeddedManifest_DoesNotReferenceOpenUpmOrNuGetForUnityPackage(string engineName)
    {
        var engine = Enum.Parse<ClientEngine>(engineName);
        var plan = Render(new UnityClientRenderer(), Spec(engine, NuGetForUnitySource.Embedded));
        var manifest = Assert.Single(plan.Files, file => file.RelativePath == "Client/Packages/manifest.json").Content;

        Assert.DoesNotContain("package.openupm.com", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("\"com.github-glitchenzo.nugetforunity\": \"4.5.0\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"dependencies\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"com.lakona.mygame.shared\": \"file:../../Shared\"", manifest, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UnityCn")]
    [InlineData("Tuanjie")]
    public void UnityClientRenderer_EmbeddedSource_RequestsNuGetForUnityArchiveExtraction(string engineName)
    {
        var engine = Enum.Parse<ClientEngine>(engineName);
        var plan = Render(new UnityClientRenderer(), Spec(engine, NuGetForUnitySource.OpenUpm));

        var archive = Assert.Single(plan.Archives ?? []);
        Assert.Equal("Lakona.Tool.Rendering.Client.TemplateAssets.NuGetForUnity.4.5.0.zip", archive.ResourceName);
        Assert.Equal("Client/Packages", archive.RelativeDestinationPath);
    }

    private static GenerationPlan Render(IClientRenderer renderer, LakonaProjectSpec spec)
    {
        var builder = new GenerationPlanBuilder("Root");
        renderer.AddFiles(spec, builder);
        return builder.Build();
    }

    private static LakonaProjectSpec Spec(
        ClientEngine engine,
        NuGetForUnitySource source = NuGetForUnitySource.OpenUpm,
        SerializerKind serializer = SerializerKind.MemoryPack,
        TransportKind transport = TransportKind.Kcp)
    {
        return new LakonaProjectSpecFactory().Create(new NewProjectOptions(
            "MyGame",
            ".",
            engine,
            transport,
            serializer,
            PersistenceKind.None,
            source,
            DeploymentProfile.None));
    }

    private static GeneratedFile AssertPath(GenerationPlan plan, string relativePath)
    {
        return Assert.Single(plan.Files, file => file.RelativePath == relativePath);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static void AssertUnitySceneHasMainCamera(string scene)
    {
        Assert.Contains("m_Name: Main Camera", scene, StringComparison.Ordinal);
        Assert.Contains("m_TagString: MainCamera", scene, StringComparison.Ordinal);
        Assert.Contains("--- !u!20 ", scene, StringComparison.Ordinal);
        Assert.Contains("Camera:", scene, StringComparison.Ordinal);
        Assert.Contains("--- !u!81 ", scene, StringComparison.Ordinal);
        Assert.Contains("AudioListener:", scene, StringComparison.Ordinal);
        Assert.Contains("m_LocalPosition: {x: 0, y: 1, z: -10}", scene, StringComparison.Ordinal);
    }

    private static string ExtractCssRule(string css, string selector)
    {
        var start = css.IndexOf(selector + " {", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing CSS rule for {selector}.");
        var end = css.IndexOf('}', start);
        Assert.True(end >= 0, $"CSS rule for {selector} is not closed.");
        return css[start..(end + 1)];
    }
}
