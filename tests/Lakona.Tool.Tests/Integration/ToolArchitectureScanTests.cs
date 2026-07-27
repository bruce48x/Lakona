using System.Text;
using System.Text.Json;
using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Execution;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Client;
using Lakona.Tool.Rendering.Common;
using Lakona.Tool.Rendering.Docs;
using Lakona.Tool.Rendering.Operations;
using Lakona.Tool.Rendering.Server;
using Lakona.Tool.Rendering.Shared;
using Xunit;

namespace Lakona.Tool.Tests.Integration;

public sealed class ToolArchitectureScanTests
{
    private static readonly string ForbiddenGeneratedGlueFile = string.Concat("Generated", "Service", "Endpoints");
    private static readonly string ForbiddenHotfixMarkerCall = string.Concat("Hotfix", "Rpc", "Service", "(");
    private static readonly string ForbiddenGameEndpointType = string.Concat("Game", "Endpoint", "Name");
    private static readonly string ForbiddenSessionEndpointType = string.Concat("Session", "Endpoint", "Key");
    private static readonly string ForbiddenGameSessionKeyType = string.Concat("Game", "Session", "Key");
    private static readonly string ForbiddenGameSessionContractField = string.Concat("Game", "Session", "Key", " Session");
    private static readonly string ForbiddenGameSessionFormatter = string.Concat("Game", "Session", "Key", "Memory", "Pack", "Formatter");
    private static readonly string ForbiddenBoundHook = string.Concat("On", "Endpoint", "Bound");
    private static readonly string ForbiddenDisconnectedHook = string.Concat("On", "Endpoint", "Disconnected");
    private static readonly string ForbiddenExpiredHook = string.Concat("On", "Endpoint", "Expired");
    private static readonly string ForbiddenAppEventAdapterName = string.Concat("Hotfix", "Runtime", "Events");
    private static readonly string ForbiddenEventAdapterSuffix = string.Concat("Runtime", "Events");
    private static readonly string ForbiddenRoomLoopName = string.Concat("Room", "Runtime");
    private static readonly string ForbiddenMatchLoopHostName = string.Concat("Matchmaking", "Hosted", "Service");
    private static readonly string ForbiddenDispatchCall = string.Concat("HotfixDispatch", ".Invoke");
    private static readonly string ForbiddenContractAttribute = string.Concat("Hotfix", "Actor", "Contract");
    private static readonly string ForbiddenChatRoomContractInterface = string.Concat("IChatRoom", "Actor", "Contract");
    private static readonly string ForbiddenStableActorRefsProperty = string.Concat("LakonaHotfixGenerateStable", "ActorRefs");
    private static readonly string RemovedRpcGenerationFile = string.Concat("Lakona", "Rpc", "Generation", ".cs");
    private static readonly string RemovedGameClientRuntimeProperty = string.Concat("Lakona", "Game", "Client", "Runtime");
    private static readonly string RemovedGameClientPlatformProperty = string.Concat("Lakona", "Game", "Client", "Platform");
    private static readonly string RemovedGameClientGameVersionProperty = string.Concat("Lakona", "Game", "Client", "Game", "Version");

    [Fact]
    public void ToolSource_DoesNotContainStarterPipelineArtifacts()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceText = ReadAllTextFiles(Path.Combine(repositoryRoot, "src", "Lakona.Tool"))
            + ReadAllTextFiles(Path.Combine(repositoryRoot, "tests", "Lakona.Tool.Tests"));

        Assert.DoesNotContain(string.Concat("Rpc", "Starter"), sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Starter", "Template"), sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Starter", "Paths"), sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("AugmentProjectWithLakona", "Game"), sourceText, StringComparison.Ordinal);
    }

    [Fact]
    public void RootReadme_DoesNotDocumentRemovedActorLifecycleApis()
    {
        var repositoryRoot = FindRepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(repositoryRoot, "README.md"));

        Assert.DoesNotContain(string.Concat("Ensure", "Local", "Actor"), readme, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Actor", "Spawn", "Attribute"), readme, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Actor", "Destroy", "Attribute"), readme, StringComparison.Ordinal);
    }

    [Fact]
    public void HotfixRuntimeAndCompiler_HaveSingleOwningPackages()
    {
        var repositoryRoot = FindRepositoryRoot();
        var serverRoot = Path.Combine(repositoryRoot, "src", "Lakona.Game.Server");
        var abstractionsRoot = Path.Combine(repositoryRoot, "src", "Lakona.Game.Server.Hotfix.Abstractions");
        var generatorProject = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Lakona.Game.Server.Hotfix.Generators",
            "Lakona.Game.Server.Hotfix.Generators.csproj"));
        var abstractionsProject = File.ReadAllText(Path.Combine(
            abstractionsRoot,
            "Lakona.Game.Server.Hotfix.Abstractions.csproj"));
        var serverProject = File.ReadAllText(Path.Combine(serverRoot, "Lakona.Game.Server.csproj"));

        Assert.False(File.Exists(Path.Combine(
            repositoryRoot,
            "src",
            "Lakona.Game.Server.Hotfix",
            "Lakona.Game.Server.Hotfix.csproj")));
        Assert.True(File.Exists(Path.Combine(serverRoot, "Hotfix", "Runtime", "HotfixManager.cs")));
        Assert.Contains("<IsPackable>false</IsPackable>", generatorProject, StringComparison.Ordinal);
        Assert.Contains("IncludeHotfixGeneratorInPackage", abstractionsProject, StringComparison.Ordinal);
        Assert.Contains("PackagePath=\"analyzers/dotnet/cs\"", abstractionsProject, StringComparison.Ordinal);
        Assert.Contains("buildTransitive\\Lakona.Game.Server.Hotfix.Abstractions.props", abstractionsProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona.Game.Server.Hotfix.csproj", serverProject, StringComparison.Ordinal);
    }

    [Fact]
    public void RpcUnitySamples_OwnContractsOutsideClientProjects()
    {
        var repositoryRoot = FindRepositoryRoot();
        var samplesRoot = Path.Combine(repositoryRoot, "samples");
        var sampleNames = new[]
        {
            "Rpc.Unity.Json.Websocket",
            "Rpc.Unity.MemoryPack.Kcp",
            "Rpc.Unity.MemoryPack.Tcp"
        };

        foreach (var sampleName in sampleNames)
        {
            var sampleRoot = Path.Combine(samplesRoot, sampleName);
            var sharedRoot = Path.Combine(sampleRoot, "Shared");
            var oldEmbeddedContracts = Path.Combine(
                sampleRoot,
                "Client",
                "Packages",
                "com.samples.contracts");
            var serverProject = File.ReadAllText(Path.Combine(sampleRoot, "Server", "Server", "Server.csproj"));
            var manifestPath = Path.Combine(sampleRoot, "Client", "Packages", "manifest.json");

            Assert.True(File.Exists(Path.Combine(sharedRoot, "package.json")));
            Assert.NotEmpty(Directory.GetFiles(sharedRoot, "*.cs", SearchOption.TopDirectoryOnly));
            Assert.Empty(Directory.Exists(oldEmbeddedContracts)
                ? Directory.GetFiles(oldEmbeddedContracts, "*", SearchOption.AllDirectories)
                : []);
            Assert.Contains("..\\..\\Shared\\**\\*.cs", serverProject, StringComparison.Ordinal);

            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            Assert.Equal(
                "file:../../Shared",
                manifest.RootElement.GetProperty("dependencies")
                    .GetProperty("com.samples.contracts")
                    .GetString());

            var clientRoot = Path.Combine(sampleRoot, "Client");
            var editorTestProjectPath = Path.Combine(clientRoot, "Game.Rpc.Tests.Editor.csproj");
            if (!File.Exists(editorTestProjectPath))
                continue;

            var editorTestProject = System.Xml.Linq.XDocument.Load(editorTestProjectPath);
            var projectNamespace = editorTestProject.Root!.Name.Namespace;
            var compileIncludes = editorTestProject
                .Descendants(projectNamespace + "Compile")
                .Select(static element => element.Attribute("Include")?.Value)
                .Where(static include => !string.IsNullOrWhiteSpace(include));
            foreach (var include in compileIncludes)
            {
                Assert.True(
                    File.Exists(Path.Combine(clientRoot, include!)),
                    $"Unity editor test project references missing source: {sampleName}/{include}");
            }
        }

    }

    [Fact]
    public async Task NewProject_UsesHealthEndpointsAndDoesNotGenerateLegacyCheckCommands()
    {
        var repositoryRoot = FindRepositoryRoot();
        var toolSourceText = ReadAllTextFiles(Path.Combine(repositoryRoot, "src", "Lakona.Tool"));
        var parentRoot = Path.Combine(Path.GetTempPath(), "lakona-tool-health-endpoint-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parentRoot);
        try
        {
            var spec = new LakonaProjectSpecFactory().Create(new NewProjectOptions(
                "MyGame",
                parentRoot,
                ClientEngine.Unity,
                TransportKind.Kcp,
                SerializerKind.MemoryPack,
                NuGetForUnitySource.OpenUpm,
                DeploymentProfile.None));
            var generator = CreateGenerator();

            var result = await generator.GenerateAsync(spec, TestContext.Current.CancellationToken);

            var generatedText = ReadAllTextFiles(spec.Layout.RootPath);
            Assert.Contains("/_lakona/health/ready", toolSourceText, StringComparison.Ordinal);
            Assert.Contains("/_lakona/health/ready", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("--readiness-check", toolSourceText, StringComparison.Ordinal);
            Assert.DoesNotContain("--readiness-check", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("--liveness-check", toolSourceText, StringComparison.Ordinal);
            Assert.DoesNotContain("--liveness-check", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("--lakona-game-check", toolSourceText, StringComparison.Ordinal);
            Assert.DoesNotContain("--lakona-game-check", generatedText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(parentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task NewProject_DoesNotGenerateLegacyStarterLayout()
    {
        var parentRoot = Path.Combine(Path.GetTempPath(), "lakona-tool-architecture-scan-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parentRoot);
        try
        {
            var spec = new LakonaProjectSpecFactory().Create(new NewProjectOptions(
                "MyGame",
                parentRoot,
                ClientEngine.Unity,
                TransportKind.Kcp,
                SerializerKind.MemoryPack,
                NuGetForUnitySource.OpenUpm,
                DeploymentProfile.None));
            var generator = CreateGenerator();

            var result = await generator.GenerateAsync(spec, TestContext.Current.CancellationToken);

            Assert.False(Directory.Exists(Path.Combine(spec.Layout.RootPath, "Server", "Server")));
            Assert.True(File.Exists(Path.Combine(spec.Layout.RootPath, "Server", "App", "Server.App.csproj")));
            Assert.False(Directory.Exists(Path.Combine(spec.Layout.RootPath, "Client", "Assets", "Scripts", "Rpc", "Generated")));

            var generatedText = ReadAllTextFiles(spec.Layout.RootPath);
            Assert.DoesNotContain(string.Concat("Rpc", "Starter"), generatedText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(parentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task NewProject_DoesNotGenerateManualHotfixServiceGlueOrEndpointSessionNames()
    {
        var parentRoot = Path.Combine(Path.GetTempPath(), "lakona-tool-session-shape-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parentRoot);
        try
        {
            var spec = new LakonaProjectSpecFactory().Create(new NewProjectOptions(
                "MyGame",
                parentRoot,
                ClientEngine.Godot,
                TransportKind.WebSocket,
                SerializerKind.Json,
                NuGetForUnitySource.OpenUpm,
                DeploymentProfile.None));
            var generator = CreateGenerator();

            var result = await generator.GenerateAsync(spec, TestContext.Current.CancellationToken);

            Assert.False(File.Exists(Path.Combine(spec.Layout.RootPath, "Server", "App", "Services", $"{ForbiddenGeneratedGlueFile}.cs")));
            Assert.False(Directory.Exists(Path.Combine(spec.Layout.RootPath, "Server", "App", "Hotfix")));
            Assert.True(File.Exists(Path.Combine(spec.Layout.RootPath, "Server", "App", "Game", "GameWorldMessages.cs")));
            Assert.False(Directory.Exists(Path.Combine(spec.Layout.RootPath, "Server", "App", "Chat")));

            var generatedText = ReadAllTextFiles(spec.Layout.RootPath);
            var generatedSharedText = ReadAllTextFiles(Path.Combine(spec.Layout.RootPath, "Shared"));
            Assert.DoesNotContain("Chat", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("<CompilerVisibleProperty", generatedText, StringComparison.Ordinal);
            Assert.Contains("await _gameServer.StartSessionAsync", generatedText, StringComparison.Ordinal);
            Assert.Contains("RPC services that target actors whose", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(ForbiddenContractAttribute, generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(ForbiddenChatRoomContractInterface, generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(ForbiddenStableActorRefsProperty, generatedText, StringComparison.Ordinal);
            Assert.Contains("public GameService(ActorAccess actors, ILakonaGameServer gameServer)", generatedText, StringComparison.Ordinal);
            Assert.Contains("private readonly ActorAccess _actors;", generatedText, StringComparison.Ordinal);
            Assert.Contains(".Startup<GameWorldActor>(GameWorldIds.Global)", generatedText, StringComparison.Ordinal);
            Assert.Contains(".CallAsync(", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("call.Actors is node-local", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("var starterNodeLocalActors = call.Actors;", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("starterNodeLocalActors.AskAsync", generatedText, StringComparison.Ordinal);
            Assert.Contains("actors.Route<RoomActor>(roomId).CallAsync", generatedText, StringComparison.Ordinal);
            Assert.Contains("actors.Local<RoomActor>(roomId)", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("rooms.Remote(nodeId, roomId)", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("starterLocalActors", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("var localActors = call.Actors;", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("localActors.AskAsync", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("call.Actors.AskAsync", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(ForbiddenGameSessionKeyType, generatedSharedText, StringComparison.Ordinal);
            Assert.DoesNotContain(ForbiddenGameSessionContractField, generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(ForbiddenGameSessionFormatter, generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("scoped ref", generatedSharedText, StringComparison.Ordinal);
            Assert.DoesNotContain(ForbiddenGeneratedGlueFile, generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(ForbiddenHotfixMarkerCall, generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(ForbiddenGameEndpointType, generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(ForbiddenSessionEndpointType, generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(ForbiddenBoundHook, generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(ForbiddenDisconnectedHook, generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(ForbiddenExpiredHook, generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("RpcSession.Disconnected +=", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("AddLakonaGame(", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("AddLakonaGameSessionHotfixLifecycle", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("UseGeneratedHotfixServices", generatedText, StringComparison.Ordinal);
            Assert.Contains("public static class HotfixStartup", generatedText, StringComparison.Ordinal);
            Assert.Contains("[HotfixStartup]", generatedText, StringComparison.Ordinal);
            Assert.Contains("[HotfixConfigureActors]", generatedText, StringComparison.Ordinal);
            Assert.Contains("ConfigureActors(ActorHostBuilder actors)", generatedText, StringComparison.Ordinal);
            Assert.Contains("actors.RegisterStartup<GameWorldActor, string>", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(string.Concat("[Hotfix", "Fea", "ture(\"chat\")]"), generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(string.Concat("HotfixGame", "Fea", "ture"), generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(".GetRequiredService<ActorHosting>()", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(".CreateAsync<GameWorldActor>", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(".EnsureAsync<GameWorldActor>", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(string.Concat("Ensure", "Local", "Actor"), generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(string.Concat("Create", "Local", "Async<GameWorldActor>"), generatedText, StringComparison.Ordinal);
            Assert.Contains("GameSessionLifecycle", generatedText, StringComparison.Ordinal);
            Assert.Contains("[HotfixLifecycle(typeof(IGameSessionLifecycle))]", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("IChatRuntimeService", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("ChatRuntimeContracts", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("ChatHotfixRuntimeEvents", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(ForbiddenAppEventAdapterName, generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(ForbiddenEventAdapterSuffix, generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(ForbiddenRoomLoopName, generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(ForbiddenMatchLoopHostName, generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("ChatSessionLifecycleBridge", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("LifecycleService", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("ChatPresenceLifecycleHandler", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("namespace Server.App.Lifecycle", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(ForbiddenDispatchCall, generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("RpcNotificationBindings", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("callbacks.Add", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(".HandshakeAsync(", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("public RpcClient RpcClient", generatedText, StringComparison.Ordinal);
            Assert.Contains("new LakonaGameClient(options, this)", generatedText, StringComparison.Ordinal);
            Assert.Contains(".Api.Shared.Game", generatedText, StringComparison.Ordinal);

            var appsettings = File.ReadAllText(Path.Combine(spec.Layout.RootPath, "Server", "App", "appsettings.json"));
            Assert.DoesNotContain(string.Concat("\"", "Fea", "ture", "\""), appsettings, StringComparison.Ordinal);
            Assert.Contains("\"ActorHosts\"", appsettings, StringComparison.Ordinal);
            Assert.DoesNotContain("\"StartupActors\"", appsettings, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(parentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task NewProject_JsonSerializer_DoesNotGenerateMemoryPackProjectArtifacts()
    {
        var parentRoot = Path.Combine(Path.GetTempPath(), "lakona-tool-json-serializer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parentRoot);
        try
        {
            var spec = new LakonaProjectSpecFactory().Create(new NewProjectOptions(
                "MyGame",
                parentRoot,
                ClientEngine.Godot,
                TransportKind.WebSocket,
                SerializerKind.Json,
                NuGetForUnitySource.OpenUpm,
                DeploymentProfile.Compose));
            var generator = CreateGenerator();

            var result = await generator.GenerateAsync(spec, TestContext.Current.CancellationToken);

            var generatedText = ReadAllTextFiles(spec.Layout.RootPath);
            var appsettings = File.ReadAllText(Path.Combine(spec.Layout.RootPath, "Server", "App", "appsettings.json"));
            using var document = JsonDocument.Parse(appsettings);
            var lakona = document.RootElement.GetProperty("Lakona");
            var endpoint = lakona.GetProperty("Endpoints")[0];

            Assert.Equal("json", endpoint.GetProperty("Serializer").GetString());
            Assert.False(lakona.TryGetProperty("Cluster", out _));
            Assert.DoesNotContain("MemoryPackable", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("MemoryPackOrder", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("using MemoryPack", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("MemoryPack.Generator", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("MemoryPack.Core", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("Lakona.Rpc.Serializer.MemoryPack", generatedText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(parentRoot, recursive: true);
        }
    }

    [Fact]
    public void GodotChatSample_UsesSessionConnectionWithoutBindingChatCallback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sampleText = ReadAllTextFiles(Path.Combine(repositoryRoot, "samples", "Game.Godot.Chat"));
        var chatService = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "samples",
            "Game.Godot.Chat",
            "Server",
            "Hotfix",
            "Chat",
            "ChatService.cs"));
        var loginService = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "samples",
            "Game.Godot.Chat",
            "Server",
            "Hotfix",
            "Login",
            "LoginService.cs"));

        Assert.Contains("public ChatService(ActorAccess actors, ILogger<ChatService> logger, ChatNotifier notifications)", chatService, StringComparison.Ordinal);
        Assert.Contains("private readonly ActorAccess _actors;", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("BindCurrentSessionAsync", chatService, StringComparison.Ordinal);
        Assert.Contains(".Startup<ChatRoomActor>(ChatRoomIds.Global)", chatService, StringComparison.Ordinal);
        Assert.Contains("static behavior => behavior.SendAsync", chatService, StringComparison.Ordinal);
        Assert.Contains("var text = call.Request.Text ?? \"\";", chatService, StringComparison.Ordinal);
        Assert.Contains("_logger.LogInformation(\"Sending {CharacterCount} characters\", text.Length);", chatService, StringComparison.Ordinal);
        Assert.Contains("_notifications.Message(result.Recipients, result.Message);", chatService, StringComparison.Ordinal);
        Assert.Contains("ChatServiceCall<ChatBindRequest>", chatService, StringComparison.Ordinal);
        Assert.Contains("ChatServiceCall<ChatSendRequest>", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Request.Text.Length", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("FilterMessage(call.Request.Text ?? \"\")", chatService, StringComparison.Ordinal);
        Assert.Contains("public LoginService(ActorAccess actors, ILakonaGameServer gameServer, ChatNotifier notifications)", loginService, StringComparison.Ordinal);
        Assert.Contains("private readonly ActorAccess _actors;", loginService, StringComparison.Ordinal);
        Assert.Contains(".Startup<ChatRoomActor>(ChatRoomIds.Global)", loginService, StringComparison.Ordinal);
        Assert.Contains("static behavior => behavior.LoginAsync", loginService, StringComparison.Ordinal);
        Assert.Contains("await _gameServer.StartSessionAsync", loginService, StringComparison.Ordinal);
        Assert.Contains("LoginServiceCall<LoginRequest>", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("HotfixServiceCall<", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("HotfixServiceCall<", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("var starterNodeLocalActors = call.Actors;", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Actors is node-local", chatService, StringComparison.Ordinal);
        Assert.Contains("call.CurrentSession", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Callback", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("starterLocalActors", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("var localActors = call.Actors;", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain(".AskAsync", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain(".AskAsync", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain(".TellAsync", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("localActors.AskAsync", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Request.Session", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain(ForbiddenGameSessionKeyType, ReadAllTextFiles(Path.Combine(repositoryRoot, "samples", "Game.Godot.Chat", "Shared")), StringComparison.Ordinal);
        Assert.DoesNotContain(ForbiddenGameSessionFormatter, sampleText, StringComparison.Ordinal);
    }

    [Fact]
    public void GodotChatSample_UsesZeroTemplateHostAndHotfixStartupActor()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sampleRoot = Path.Combine(repositoryRoot, "samples", "Game.Godot.Chat");
        var appText = ReadAllTextFiles(Path.Combine(sampleRoot, "Server", "App"));
        var hotfixText = ReadAllTextFiles(Path.Combine(sampleRoot, "Server", "Hotfix"));
        var hotfixProject = File.ReadAllText(Path.Combine(sampleRoot, "Server", "Hotfix", "Server.Hotfix.csproj"));
        var program = File.ReadAllText(Path.Combine(sampleRoot, "Server", "App", "Program.cs"));
        var appsettings = File.ReadAllText(Path.Combine(sampleRoot, "Server", "App", "appsettings.json"));
        var loginClient = File.ReadAllText(Path.Combine(sampleRoot, "Client", "Scripts", "Login", "LoginClient.cs"));

        Assert.Contains("LakonaGameServer.RunAsync", program, StringComparison.Ordinal);
        Assert.Contains("RegisterEndpointTransport(\"websocket\"", program, StringComparison.Ordinal);
        Assert.Contains("RegisterEndpointSerializer(\"memorypack\"", program, StringComparison.Ordinal);
        Assert.Contains("UseClusterRpc(TcpClusterRpcTransport.Default, MemoryPackClusterRpcSerializer.Default)", program, StringComparison.Ordinal);
        Assert.Contains("Lakona.Game.Server.Hotfix.Abstractions", hotfixProject, StringComparison.Ordinal);
        Assert.Contains("Lakona.Game.Server.Hotfix.Generators.csproj", hotfixProject, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<PackageReference Include=\"Lakona.Game.Server.Hotfix.Generators\"",
            hotfixProject,
            StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("\"", "Fea", "ture", "\""), appsettings, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(appsettings);
        var sessions = document.RootElement.GetProperty("Lakona")
            .GetProperty("Sessions");
        Assert.Equal(60, sessions.GetProperty("ResumeWindowSeconds").GetInt32());
        Assert.True(document.RootElement.GetProperty("Lakona")
            .GetProperty("Endpoints")[0]
            .GetProperty("ReliablePush")
            .GetBoolean());
        var hotfix = document.RootElement.GetProperty("Lakona")
            .GetProperty("Hotfix");
        Assert.Equal("On", hotfix.GetProperty("DebugWatcher").GetString());
        Assert.DoesNotContain("AddLakonaGame(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("AddLakonaGameSessionHotfixLifecycle", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("UseGeneratedHotfixServices", appText, StringComparison.Ordinal);
        Assert.Contains("public static class HotfixStartup", hotfixText, StringComparison.Ordinal);
        Assert.Contains("[HotfixStartup]", hotfixText, StringComparison.Ordinal);
        Assert.Contains("[HotfixConfigureActors]", hotfixText, StringComparison.Ordinal);
        Assert.Contains("actors.RegisterStartup<ChatRoomActor, string>", hotfixText, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Hotfix", "Fea", "ture"), hotfixText, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("HotfixGame", "Fea", "ture"), hotfixText, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Hotfix", "Fea", "tureContext"), hotfixText, StringComparison.Ordinal);
        Assert.DoesNotContain("public override void Configure", hotfixText, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("I", "Fea", "tureMessageHandler"), hotfixText, StringComparison.Ordinal);
        Assert.DoesNotContain(".GetRequiredService<ActorHosting>()", hotfixText, StringComparison.Ordinal);
        Assert.DoesNotContain(".CreateAsync<ChatRoomActor>(ActorId.From(ChatRoomIds.Global), call.CancellationToken)", hotfixText, StringComparison.Ordinal);
        Assert.DoesNotContain(".EnsureAsync<ChatRoomActor>", hotfixText, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Ensure", "Local", "Actor"), hotfixText, StringComparison.Ordinal);
        Assert.Contains("internal sealed partial class ChatRoomBehavior", hotfixText, StringComparison.Ordinal);
        Assert.Contains("ChatSessionLifecycle", hotfixText, StringComparison.Ordinal);
        Assert.Contains("Disconnected sessions stay in the room during the retention window so a client can reconnect without flickering presence.", hotfixText, StringComparison.Ordinal);
        Assert.Contains("_notifications.ForSession<ILoginCallback>(recipient)", hotfixText, StringComparison.Ordinal);
        Assert.Contains(".OnUserJoined(member);", hotfixText, StringComparison.Ordinal);
        Assert.DoesNotContain(".OnUserJoined(member, cancellationToken)", hotfixText, StringComparison.Ordinal);
        Assert.Contains("[HotfixLifecycle(typeof(IGameSessionLifecycle))]", hotfixText, StringComparison.Ordinal);
        Assert.Contains("new ChatRoomLeaveRequest", hotfixText, StringComparison.Ordinal);
        Assert.DoesNotContain(".AskAsync", hotfixText, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Create", "Local", "Async<ChatRoomActor>"), hotfixText, StringComparison.Ordinal);
        Assert.Contains("private readonly LakonaGameClient _gameClient;", loginClient, StringComparison.Ordinal);
        Assert.Contains("_gameClient = new LakonaGameClient(options, this);", loginClient, StringComparison.Ordinal);
        Assert.Contains("_loginService = _gameClient.Api.Shared.Login;", loginClient, StringComparison.Ordinal);
        Assert.Contains("public LakonaGameClient GameClient => _gameClient;", loginClient, StringComparison.Ordinal);
        Assert.DoesNotContain("RpcNotificationBindings", loginClient, StringComparison.Ordinal);
        Assert.DoesNotContain("callbacks.Add", loginClient, StringComparison.Ordinal);
        Assert.DoesNotContain("HandshakeAsync", loginClient, StringComparison.Ordinal);
        Assert.DoesNotContain("public RpcClient RpcClient", loginClient, StringComparison.Ordinal);
    }

    [Fact]
    public void GameSamples_KeepDependenciesAtTheirOwningProjectSeams()
    {
        var repositoryRoot = FindRepositoryRoot();
        var unityRoot = Path.Combine(repositoryRoot, "samples", "Game.Unity.Agar");
        var unityShared = File.ReadAllText(Path.Combine(unityRoot, "Shared", "Shared.csproj"));
        var unityApp = File.ReadAllText(Path.Combine(unityRoot, "Server", "App", "Server.App.csproj"));
        var unityHotfix = File.ReadAllText(Path.Combine(unityRoot, "Server", "Hotfix", "Server.Hotfix.csproj"));
        var unityTests = File.ReadAllText(Path.Combine(unityRoot, "tests", "BusinessLogic.Tests", "BusinessLogic.Tests.csproj"));
        var unityPackages = File.ReadAllText(Path.Combine(unityRoot, "Client", "Assets", "packages.config"));
        var godotRoot = Path.Combine(repositoryRoot, "samples", "Game.Godot.Chat");
        var godotShared = File.ReadAllText(Path.Combine(godotRoot, "Shared", "Shared.csproj"));
        var godotApp = File.ReadAllText(Path.Combine(godotRoot, "Server", "App", "Server.App.csproj"));
        var godotClient = File.ReadAllText(Path.Combine(godotRoot, "Client", "Client.csproj"));

        Assert.DoesNotContain("Lakona.Rpc.Serializer.MemoryPack", unityShared, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona.Rpc.Server", unityShared, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona.Rpc.Analyzers", unityShared, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona.Game.Server.Hotfix", unityShared, StringComparison.Ordinal);
        Assert.DoesNotContain("LakonaRpcGenerateServer", unityShared, StringComparison.Ordinal);
        Assert.Contains("<LakonaRpcGenerateServer>true</LakonaRpcGenerateServer>", unityApp, StringComparison.Ordinal);
        Assert.DoesNotContain("src\\Lakona.Rpc.Server\\Lakona.Rpc.Server.csproj", unityApp, StringComparison.Ordinal);
        Assert.Contains("Lakona.Rpc.Analyzers.csproj", unityApp, StringComparison.Ordinal);
        Assert.Contains("Lakona.Game.Server.Hotfix.Generators.csproj", unityApp, StringComparison.Ordinal);
        Assert.DoesNotContain("MemoryPack.UnityShims", unityHotfix, StringComparison.Ordinal);
        Assert.DoesNotContain("MemoryPack.Generator", unityHotfix, StringComparison.Ordinal);
        Assert.DoesNotContain("System.IO.Hashing", unityHotfix, StringComparison.Ordinal);
        Assert.DoesNotContain("MemoryPack.UnityShims", unityTests, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona.Rpc.Transport.Tcp", unityTests, StringComparison.Ordinal);
        Assert.Contains("id=\"Lakona.Rpc.Client\"", unityPackages, StringComparison.Ordinal);
        Assert.Contains("id=\"Lakona.Rpc.Core\"", unityPackages, StringComparison.Ordinal);

        Assert.DoesNotContain("Lakona.Rpc.Serializer.MemoryPack", godotShared, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.Extensions.Hosting", godotApp, StringComparison.Ordinal);
        Assert.DoesNotContain("src\\Lakona.Rpc.Server\\Lakona.Rpc.Server.csproj", godotApp, StringComparison.Ordinal);
        Assert.DoesNotContain("src\\Lakona.Game.Cluster\\Lakona.Game.Cluster.csproj", godotApp, StringComparison.Ordinal);
        Assert.DoesNotContain("src\\Lakona.Game.Cluster.Rpc\\Lakona.Game.Cluster.Rpc.csproj", godotApp, StringComparison.Ordinal);
        Assert.DoesNotContain("src\\Lakona.Rpc.Core\\Lakona.Rpc.Core.csproj", godotClient, StringComparison.Ordinal);
        Assert.DoesNotContain("src\\Lakona.Rpc.Client\\Lakona.Rpc.Client.csproj", godotClient, StringComparison.Ordinal);
        Assert.DoesNotContain("src\\Lakona.Game.Abstractions\\Lakona.Game.Abstractions.csproj", godotClient, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeneratedArenaUsesSingleGlobalGameWorldActorId()
    {
        var parentRoot = Path.Combine(Path.GetTempPath(), "lakona-tool-game-world-id-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parentRoot);
        try
        {
            var spec = new LakonaProjectSpecFactory().Create(new NewProjectOptions(
                "MyGame",
                parentRoot,
                ClientEngine.Godot,
                TransportKind.WebSocket,
                SerializerKind.Json,
                NuGetForUnitySource.OpenUpm,
                DeploymentProfile.None));
            var generator = CreateGenerator();

            var result = await generator.GenerateAsync(spec, TestContext.Current.CancellationToken);

            var appGame = ReadAllTextFiles(Path.Combine(spec.Layout.RootPath, "Server", "App", "Game"));
            var hotfix = ReadAllTextFiles(Path.Combine(spec.Layout.RootPath, "Server", "Hotfix"));
            Assert.Contains("public const string Global = \"game-world/global\";", appGame, StringComparison.Ordinal);
            Assert.Contains("RegisterStartup<GameWorldActor, string>", hotfix, StringComparison.Ordinal);
            Assert.Contains(".Startup<GameWorldActor>(GameWorldIds.Global)", hotfix, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(parentRoot, recursive: true);
        }
    }

    [Fact]
    public void UnityAgarSample_SeparatesNodeLocalActorRuntimeFromPlacementAwareSelectors()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sampleRoot = Path.Combine(repositoryRoot, "samples", "Game.Unity.Agar");
        var readme = File.ReadAllText(Path.Combine(sampleRoot, "README.md"));
        var hotfixCode = ReadAllTextFiles(Path.Combine(sampleRoot, "Server", "Hotfix"));
        var clientScripts = ReadAllTextFiles(Path.Combine(sampleRoot, "Client", "Assets", "Scripts"));

        Assert.Contains("node-local actor runtime", readme, StringComparison.Ordinal);
        Assert.Contains("RPC service", readme, StringComparison.Ordinal);
        Assert.Contains("may be local or remote", readme, StringComparison.Ordinal);
        Assert.Contains("typed selector", readme, StringComparison.Ordinal);
        Assert.Contains(".Route<UserActor>(new UserId(", hotfixCode, StringComparison.Ordinal);
        Assert.Contains(".Startup<MatchmakingActor>(new MatchmakingQueueId(", hotfixCode, StringComparison.Ordinal);
        Assert.Contains(".Local<RoomActor>(roomId)", hotfixCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Remote(new NodeId(", hotfixCode, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Actors", hotfixCode, StringComparison.Ordinal);
        Assert.DoesNotContain("var localActors = call.Actors;", hotfixCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".AskAsync", hotfixCode, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(sampleRoot, "Client", "Assets", "Scripts", "Rpc", RemovedRpcGenerationFile)));
        Assert.False(File.Exists(Path.Combine(sampleRoot, "Client", "Assets", "Scripts", "Rpc", RemovedRpcGenerationFile + ".meta")));
        Assert.DoesNotContain(RemovedGameClientRuntimeProperty, clientScripts, StringComparison.Ordinal);
        Assert.DoesNotContain(RemovedGameClientPlatformProperty, clientScripts, StringComparison.Ordinal);
        Assert.DoesNotContain(RemovedGameClientGameVersionProperty, clientScripts, StringComparison.Ordinal);
        Assert.DoesNotContain("[assembly: LakonaRpcGenerateClient", clientScripts, StringComparison.Ordinal);
        Assert.DoesNotContain("[assembly: LakonaGameGenerateClient", clientScripts, StringComparison.Ordinal);
        Assert.DoesNotContain("RpcNotificationBindings", clientScripts, StringComparison.Ordinal);
        Assert.DoesNotContain("callbacks.Add", clientScripts, StringComparison.Ordinal);
        Assert.DoesNotContain(".HandshakeAsync(", clientScripts, StringComparison.Ordinal);
        Assert.DoesNotContain("GameClientHello", clientScripts, StringComparison.Ordinal);
    }

    [Fact]
    public void UnityAgarSample_UsesFrameworkGameServerForSessionLifecycle()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sampleRoot = Path.Combine(repositoryRoot, "samples", "Game.Unity.Agar");
        var hotfixBusinessCode = ReadAllTextFiles(Path.Combine(sampleRoot, "Server", "Hotfix"));
        var sessionDirectoryPath = Path.Combine(
            sampleRoot,
            "Server",
            "Hotfix",
            "Sessions",
            "PlayerSessionRegistry.cs");
        var registrationPath = Path.Combine(
            sampleRoot,
            "Server",
            "Hotfix",
            "Sessions",
            "PlayerSessionRegistration.cs");

        Assert.False(File.Exists(sessionDirectoryPath), "Agar hotfix should not own PlayerSessionRegistry.cs.");
        Assert.False(File.Exists(registrationPath), "Agar hotfix should not own PlayerSessionRegistration.cs.");
        Assert.Contains("call.GameServer", hotfixBusinessCode, StringComparison.Ordinal);
        Assert.Contains(".StartSessionAsync", hotfixBusinessCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".ResumeSessionAsync", hotfixBusinessCode, StringComparison.Ordinal);
        Assert.Contains(".TerminateSessionAsync", hotfixBusinessCode, StringComparison.Ordinal);
        Assert.DoesNotContain("IGameSessionRegistry", hotfixBusinessCode, StringComparison.Ordinal);
        Assert.DoesNotContain("InMemoryGameSessionRegistry", hotfixBusinessCode, StringComparison.Ordinal);
        Assert.DoesNotContain("StartNewSessionAsync", hotfixBusinessCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TryResumeAsync", hotfixBusinessCode, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerSessionRegistry", hotfixBusinessCode, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerSessionRegistration", hotfixBusinessCode, StringComparison.Ordinal);
        Assert.DoesNotContain("GetConnection", hotfixBusinessCode, StringComparison.Ordinal);
        Assert.DoesNotContain("GetByRoom", hotfixBusinessCode, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterControl", hotfixBusinessCode, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeGatewaySelector", hotfixBusinessCode, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeNodeIdentity", hotfixBusinessCode, StringComparison.Ordinal);
        Assert.DoesNotContain("EndpointDescriptorMapper", hotfixBusinessCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".AskAsync", hotfixBusinessCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".TellAsync", hotfixBusinessCode, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedContractSources_DoNotExposeServerSessionIdentityOrCSharp10Syntax()
    {
        var repositoryRoot = FindRepositoryRoot();
        var rendererText = ReadAllTextFiles(Path.Combine(repositoryRoot, "src", "Lakona.ProjectSystem", "Generation", "Rendering", "Shared"));
        var sampleSharedText = ReadAllTextFiles(Path.Combine(repositoryRoot, "samples", "Game.Godot.Chat", "Shared"));

        Assert.DoesNotContain(ForbiddenGameSessionKeyType, rendererText, StringComparison.Ordinal);
        Assert.DoesNotContain(ForbiddenGameSessionKeyType, sampleSharedText, StringComparison.Ordinal);
        Assert.DoesNotContain(ForbiddenGameSessionFormatter, rendererText, StringComparison.Ordinal);
        Assert.DoesNotContain(ForbiddenGameSessionFormatter, sampleSharedText, StringComparison.Ordinal);
        Assert.DoesNotContain("scoped ref", rendererText, StringComparison.Ordinal);
        Assert.DoesNotContain("scoped ref", sampleSharedText, StringComparison.Ordinal);
    }

    [Fact]
    public void FrameworkInternalDtos_DoNotUseEndpointSerializerMetadata()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstractionsText = ReadAllTextFiles(Path.Combine(repositoryRoot, "src", "Lakona.Game.Abstractions"));

        Assert.DoesNotContain("MemoryPackable", abstractionsText, StringComparison.Ordinal);
        Assert.DoesNotContain("MemoryPackOrder", abstractionsText, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona.Rpc.Serializer.Json", abstractionsText, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona.Rpc.Serializer.MemoryPack", abstractionsText, StringComparison.Ordinal);
        Assert.DoesNotContain("EndpointTransport", abstractionsText, StringComparison.Ordinal);
        Assert.DoesNotContain("EndpointSerializer", abstractionsText, StringComparison.Ordinal);
        Assert.DoesNotContain("ServerTimeUtc", abstractionsText, StringComparison.Ordinal);
        Assert.DoesNotContain("ServerNodeId", abstractionsText, StringComparison.Ordinal);
        Assert.DoesNotContain("DeliveryMode", abstractionsText, StringComparison.Ordinal);
        Assert.DoesNotContain("ReplaySupported", abstractionsText, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxPending", abstractionsText, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedSharedContracts_DoNotConstructFrameworkHandshakeDtos()
    {
        var repositoryRoot = FindRepositoryRoot();
        var toolText = ReadAllTextFiles(Path.Combine(repositoryRoot, "src", "Lakona.ProjectSystem", "Generation", "Rendering", "Shared"));

        Assert.DoesNotContain("new GameClientHello", toolText, StringComparison.Ordinal);
        Assert.DoesNotContain("new GameServerHello", toolText, StringComparison.Ordinal);
    }

    [Fact]
    public void GodotDailyScript_UsesGeneratedServerAppProjectLayout()
    {
        var repositoryRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repositoryRoot, "scripts", "game", "ci", "verify-lakona-tool-godot.sh");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("Server/App/Server.App.csproj", script, StringComparison.Ordinal);
        Assert.Contains("Server/Server.slnx", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Server/Server/Server.csproj", script, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet build \"$SERVER_PROJECT\"", script, StringComparison.Ordinal);
        Assert.Contains("Arena smoke ok:", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Ping ok:", script, StringComparison.Ordinal);
        Assert.Contains("godot-${TRANSPORT:0:3}-${SERIALIZER:0:3}", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolDocs_DescribeServerPackAndHotfixPackSeparately()
    {
        var repositoryRoot = FindRepositoryRoot();
        var toolReadme = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Tool", "README.md"));
        var architecture = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "tool", "generation-architecture.md"));
        var design = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "tool", "server-pack-command.md"));

        Assert.Contains("lakona-tool server pack --runtime linux-x64", toolReadme, StringComparison.Ordinal);
        Assert.Contains("lakona-tool hotfix pack", toolReadme, StringComparison.Ordinal);
        Assert.Contains("lakona-tool server pack --runtime linux-x64", architecture, StringComparison.Ordinal);
        Assert.Contains("lakona-tool hotfix pack", architecture, StringComparison.Ordinal);
        Assert.Contains("Publish trimming", design, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolAndHub_UseProjectSystemCreatorWithoutComposingGenerators()
    {
        var repositoryRoot = FindRepositoryRoot();
        var toolSources = ReadAllTextFiles(Path.Combine(repositoryRoot, "src", "Lakona.Tool"));
        var hubSources = ReadAllTextFiles(Path.Combine(repositoryRoot, "src", "Lakona.Hub"));

        Assert.Contains("new LakonaProjectCreator()", toolSources, StringComparison.Ordinal);
        Assert.Contains("LakonaProjectCreator projectCreator", hubSources, StringComparison.Ordinal);
        Assert.DoesNotContain("new LakonaProjectGenerator", toolSources, StringComparison.Ordinal);
        Assert.DoesNotContain("new LakonaProjectGenerator", hubSources, StringComparison.Ordinal);
        Assert.DoesNotContain("new LakonaProjectPlanBuilder", toolSources, StringComparison.Ordinal);
        Assert.DoesNotContain("new LakonaProjectPlanBuilder", hubSources, StringComparison.Ordinal);
        Assert.DoesNotContain("new TransactionalOutputWriter", toolSources, StringComparison.Ordinal);
        Assert.DoesNotContain("new TransactionalOutputWriter", hubSources, StringComparison.Ordinal);
    }

    private static LakonaProjectGenerator CreateGenerator()
    {
        return new LakonaProjectGenerator(
            new LakonaProjectPlanBuilder(
                [
                    new GitRenderer(),
                    new SharedProjectRenderer(),
                    new ServerAppRenderer(),
                    new HotfixRenderer(),
                    new OperationsRenderer(),
                    new GeneratedProjectGuideRenderer()
                ],
                [new UnityClientRenderer(), new GodotClientRenderer(), new ConsoleClientRenderer()]),
            new GenerationExecutor(new TransactionalOutputWriter()),
            new GitInitializer(new GitUnavailableRunner()));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Lakona.Tool"))
                && Directory.Exists(Path.Combine(directory.FullName, "tests", "Lakona.Tool.Tests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string ReadAllTextFiles(string root)
    {
        var builder = new StringBuilder();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Where(path => IsTextSourceFile(path) && !IsBuildOutputPath(root, path))
                     .Order(StringComparer.Ordinal))
        {
            builder.AppendLine(File.ReadAllText(path));
        }

        return builder.ToString();
    }

    private static bool IsTextSourceFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension is ".cs" or ".csproj" or ".json" or ".md" or ".slnx" or ".props" or ".asmdef" or ".config" or ".tscn" or ".tres" or ".xml" or ".txt";
    }

    private static bool IsBuildOutputPath(string root, string path)
    {
        var relativePath = Path.GetRelativePath(root, path);
        var segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        return segments.Any(segment =>
            StringComparer.OrdinalIgnoreCase.Equals(segment, "bin")
            || StringComparer.OrdinalIgnoreCase.Equals(segment, "obj")
            || StringComparer.OrdinalIgnoreCase.Equals(segment, "Library")
            || StringComparer.OrdinalIgnoreCase.Equals(segment, "Temp")
            || StringComparer.Ordinal.Equals(segment, ".godot")
            || StringComparer.Ordinal.Equals(segment, ".import"));
    }

    private static void AssertChatRoomIdContract(string projectRoot, bool expectStartupActors)
    {
        var appChat = ReadAllTextFiles(Path.Combine(projectRoot, "Server", "App", "Chat"));
        var hotfixChat = ReadAllTextFiles(Path.Combine(projectRoot, "Server", "Hotfix"));

        Assert.Contains("public const string Global = \"chat-room/global\";", appChat, StringComparison.Ordinal);
        if (expectStartupActors)
        {
            Assert.Contains("public static class HotfixStartup", hotfixChat, StringComparison.Ordinal);
            Assert.Contains("[HotfixStartup]", hotfixChat, StringComparison.Ordinal);
            Assert.Contains("[HotfixConfigureActors]", hotfixChat, StringComparison.Ordinal);
            Assert.Contains("ConfigureActors(ActorHostBuilder actors)", hotfixChat, StringComparison.Ordinal);
            Assert.Contains("RegisterStartup<ChatRoomActor, string>", hotfixChat, StringComparison.Ordinal);
            Assert.DoesNotContain(".GetRequiredService<ActorHosting>()", hotfixChat, StringComparison.Ordinal);
            Assert.DoesNotContain(".CreateAsync<ChatRoomActor>", hotfixChat, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(".GetRequiredService<ActorHosting>()", hotfixChat, StringComparison.Ordinal);
            Assert.Contains(".CreateAsync<ChatRoomActor>(ActorId.From(ChatRoomIds.Global), call.CancellationToken)", hotfixChat, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(".EnsureAsync<ChatRoomActor>", hotfixChat, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Ensure", "Local", "Actor"), hotfixChat, StringComparison.Ordinal);
        Assert.Contains(".Startup<ChatRoomActor>(ChatRoomIds.Global)", hotfixChat, StringComparison.Ordinal);
        Assert.DoesNotContain("private const string RoomKey", hotfixChat, StringComparison.Ordinal);
        Assert.DoesNotContain(".Get(\"global\")", hotfixChat, StringComparison.Ordinal);
    }

    private static void AssertBefore(string text, string expectedBefore, string expectedAfter)
    {
        var beforeIndex = text.IndexOf(expectedBefore, StringComparison.Ordinal);
        var afterIndex = text.IndexOf(expectedAfter, StringComparison.Ordinal);

        Assert.True(beforeIndex >= 0, $"Expected to find '{expectedBefore}'.");
        Assert.True(afterIndex >= 0, $"Expected to find '{expectedAfter}'.");
        Assert.True(beforeIndex < afterIndex, $"Expected '{expectedBefore}' to appear before '{expectedAfter}'.");
    }

    private sealed class GitUnavailableRunner : IGitCommandRunner
    {
        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            string[] arguments,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new GitCommandResult(1, "", ""));
        }
    }
}
