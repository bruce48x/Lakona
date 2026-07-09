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
        Assert.DoesNotContain(string.Concat("ULink", "RPC"), sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("ULink", "Game"), sourceText, StringComparison.Ordinal);
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
                PersistenceKind.None,
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
                PersistenceKind.None,
                NuGetForUnitySource.OpenUpm,
                DeploymentProfile.None));
            var generator = CreateGenerator();

            var result = await generator.GenerateAsync(spec, TestContext.Current.CancellationToken);

            Assert.False(Directory.Exists(Path.Combine(spec.Layout.RootPath, "Server", "Server")));
            Assert.True(File.Exists(Path.Combine(spec.Layout.RootPath, "Server", "App", "Server.App.csproj")));
            Assert.False(Directory.Exists(Path.Combine(spec.Layout.RootPath, "Client", "Assets", "Scripts", "Rpc", "Generated")));

            var generatedText = ReadAllTextFiles(spec.Layout.RootPath);
            Assert.DoesNotContain(string.Concat("ULink", "RPC"), generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(string.Concat("ULink", "Game"), generatedText, StringComparison.Ordinal);
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
                PersistenceKind.None,
                NuGetForUnitySource.OpenUpm,
                DeploymentProfile.None));
            var generator = CreateGenerator();

            var result = await generator.GenerateAsync(spec, TestContext.Current.CancellationToken);

            Assert.False(File.Exists(Path.Combine(spec.Layout.RootPath, "Server", "App", "Services", $"{ForbiddenGeneratedGlueFile}.cs")));
            Assert.False(Directory.Exists(Path.Combine(spec.Layout.RootPath, "Server", "App", "Hotfix")));
            Assert.True(File.Exists(Path.Combine(spec.Layout.RootPath, "Server", "App", "Chat", "ChatRoomMessages.cs")));
            Assert.False(File.Exists(Path.Combine(spec.Layout.RootPath, "Server", "App", "Chat", "ChatRoomActorContracts.cs")));

            var generatedText = ReadAllTextFiles(spec.Layout.RootPath);
            var generatedSharedText = ReadAllTextFiles(Path.Combine(spec.Layout.RootPath, "Shared"));
            Assert.Contains("<CompilerVisibleProperty Include=\"LakonaRpcGenerateServer\" />", generatedText, StringComparison.Ordinal);
            Assert.Contains("<CompilerVisibleProperty Include=\"LakonaRpcServerGeneratedNamespace\" />", generatedText, StringComparison.Ordinal);
            Assert.Contains("await _gameServer.BindCurrentSessionAsync", generatedText, StringComparison.Ordinal);
            Assert.Contains("RPC services that target actors whose", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(ForbiddenContractAttribute, generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(ForbiddenChatRoomContractInterface, generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(ForbiddenStableActorRefsProperty, generatedText, StringComparison.Ordinal);
            Assert.Contains("public LoginService(ChatRoomActors rooms, ILakonaGameServer gameServer)", generatedText, StringComparison.Ordinal);
            Assert.Contains("private readonly ChatRoomActors _rooms;", generatedText, StringComparison.Ordinal);
            Assert.Contains(".Route(ChatRoomIds.Global)", generatedText, StringComparison.Ordinal);
            Assert.Contains(".CallAsync(", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("call.Actors is node-local", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("var starterNodeLocalActors = call.Actors;", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("starterNodeLocalActors.AskAsync", generatedText, StringComparison.Ordinal);
            Assert.Contains("rooms.Route(roomId).CallAsync", generatedText, StringComparison.Ordinal);
            Assert.Contains("rooms.Local(roomId)", generatedText, StringComparison.Ordinal);
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
            Assert.Contains("actors.RegisterStartup(", generatedText, StringComparison.Ordinal);
            Assert.Contains("ActorStartupPlan.Create<ChatRoomActor>(ActorId.From(ChatRoomIds.Global))", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(string.Concat("[Hotfix", "Fea", "ture(\"chat\")]"), generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(string.Concat("HotfixGame", "Fea", "ture"), generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(".GetRequiredService<ActorHosting>()", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(".CreateAsync<ChatRoomActor>", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(".EnsureAsync<ChatRoomActor>", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(string.Concat("Ensure", "Local", "Actor"), generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(string.Concat("Create", "Local", "Async<ChatRoomActor>"), generatedText, StringComparison.Ordinal);
            Assert.Contains("ChatSessionLifecycle", generatedText, StringComparison.Ordinal);
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
            Assert.Contains("gameClient.Api.Shared", generatedText, StringComparison.Ordinal);

            var appsettings = File.ReadAllText(Path.Combine(spec.Layout.RootPath, "Server", "App", "appsettings.json"));
            Assert.DoesNotContain(string.Concat("\"", "Fea", "ture", "\""), appsettings, StringComparison.Ordinal);
            Assert.Contains("\"ActorHosts\"", appsettings, StringComparison.Ordinal);
            Assert.Contains("\"StartupActors\"", appsettings, StringComparison.Ordinal);
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
                PersistenceKind.None,
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
    public void GodotChatSample_BindsChatCallbackThroughFrameworkSessionRegistry()
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

        Assert.Contains("public ChatService(ChatRoomActors rooms, ILakonaGameServer gameServer, ILogger<ChatService> logger)", chatService, StringComparison.Ordinal);
        Assert.Contains("private readonly ChatRoomActors _rooms;", chatService, StringComparison.Ordinal);
        Assert.Contains("await _gameServer.BindCurrentSessionAsync", chatService, StringComparison.Ordinal);
        Assert.Contains(".Route(ChatRoomIds.Global)", chatService, StringComparison.Ordinal);
        Assert.Contains("ChatRoomBehavior.SendAsync", chatService, StringComparison.Ordinal);
        Assert.Contains("ChatRoomBehavior.BindChatAsync", chatService, StringComparison.Ordinal);
        Assert.Contains("var text = call.Request.Text ?? \"\";", chatService, StringComparison.Ordinal);
        Assert.Contains("_logger.LogInformation(\"Sending {CharacterCount} characters\", text.Length);", chatService, StringComparison.Ordinal);
        Assert.Contains("await BindChatCallbackAsync(call.ConnectionId, call.Callback);", chatService, StringComparison.Ordinal);
        Assert.Contains("private async ValueTask BindChatCallbackAsync(string connectionId, IChatCallback callback)", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Request.Text.Length", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("FilterMessage(call.Request.Text ?? \"\")", chatService, StringComparison.Ordinal);
        Assert.Contains("public LoginService(ChatRoomActors rooms, ILakonaGameServer gameServer)", loginService, StringComparison.Ordinal);
        Assert.Contains("private readonly ChatRoomActors _rooms;", loginService, StringComparison.Ordinal);
        Assert.Contains(".Route(ChatRoomIds.Global)", loginService, StringComparison.Ordinal);
        Assert.Contains("ChatRoomBehavior.LoginAsync", loginService, StringComparison.Ordinal);
        Assert.Contains("await _gameServer.StartSessionAsync", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("var starterNodeLocalActors = call.Actors;", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Actors is node-local", chatService, StringComparison.Ordinal);
        Assert.Contains("call.ConnectionId", chatService, StringComparison.Ordinal);
        Assert.Contains("call.Callback", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("starterLocalActors", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("var localActors = call.Actors;", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain(".AskAsync", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain(".AskAsync", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain(".TellAsync", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("localActors.AskAsync", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Request.Session", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain(ForbiddenGameSessionKeyType, ReadAllTextFiles(Path.Combine(repositoryRoot, "samples", "Game.Godot.Chat", "Shared")), StringComparison.Ordinal);
        Assert.DoesNotContain(ForbiddenGameSessionContractField, sampleText, StringComparison.Ordinal);
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

        Assert.Equal("using Lakona.Game.Server.Hosting;\n\nreturn await LakonaGameServer.RunAsync(args);\n", program);
        Assert.Contains("Lakona.Game.Server.Hotfix.Generators", hotfixProject, StringComparison.Ordinal);
        Assert.Contains("OutputItemType=\"Analyzer\"", hotfixProject, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("\"", "Fea", "ture", "\""), appsettings, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(appsettings);
        var cleanup = document.RootElement.GetProperty("Lakona")
            .GetProperty("Sessions")
            .GetProperty("Cleanup");
        Assert.Equal(30, cleanup.GetProperty("DisconnectedRetentionSeconds").GetInt32());
        var hotfix = document.RootElement.GetProperty("Lakona")
            .GetProperty("Hotfix");
        Assert.Equal("On", hotfix.GetProperty("DebugWatcher").GetString());
        Assert.DoesNotContain("AddLakonaGame(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("AddLakonaGameSessionHotfixLifecycle", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("UseGeneratedHotfixServices", appText, StringComparison.Ordinal);
        Assert.Contains("public static class HotfixStartup", hotfixText, StringComparison.Ordinal);
        Assert.Contains("[HotfixStartup]", hotfixText, StringComparison.Ordinal);
        Assert.Contains("[HotfixConfigureActors]", hotfixText, StringComparison.Ordinal);
        Assert.Contains("actors.RegisterStartup(", hotfixText, StringComparison.Ordinal);
        Assert.Contains("ActorStartupPlan.Create<ChatRoomActor>(ActorId.From(ChatRoomIds.Global))", hotfixText, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Hotfix", "Fea", "ture"), hotfixText, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("HotfixGame", "Fea", "ture"), hotfixText, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Hotfix", "Fea", "tureContext"), hotfixText, StringComparison.Ordinal);
        Assert.DoesNotContain("public override void Configure", hotfixText, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("I", "Fea", "tureMessageHandler"), hotfixText, StringComparison.Ordinal);
        Assert.DoesNotContain(".GetRequiredService<ActorHosting>()", hotfixText, StringComparison.Ordinal);
        Assert.DoesNotContain(".CreateAsync<ChatRoomActor>(ActorId.From(ChatRoomIds.Global), call.CancellationToken)", hotfixText, StringComparison.Ordinal);
        Assert.DoesNotContain(".EnsureAsync<ChatRoomActor>", hotfixText, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Ensure", "Local", "Actor"), hotfixText, StringComparison.Ordinal);
        Assert.Contains("internal static partial class ChatRoomBehavior", hotfixText, StringComparison.Ordinal);
        Assert.Contains("ChatSessionLifecycle", hotfixText, StringComparison.Ordinal);
        Assert.Contains("Disconnected sessions stay in the room during the retention window so a client can reconnect without flickering presence.", hotfixText, StringComparison.Ordinal);
        Assert.Contains("Callback exceptions are ignored so one stale client does not prevent other clients from receiving room events.", hotfixText, StringComparison.Ordinal);
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
    public async Task GeneratedAndSampleChatRoomsUseSingleGlobalActorId()
    {
        var repositoryRoot = FindRepositoryRoot();
        var parentRoot = Path.Combine(Path.GetTempPath(), "lakona-tool-chat-room-id-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parentRoot);
        try
        {
            var spec = new LakonaProjectSpecFactory().Create(new NewProjectOptions(
                "MyGame",
                parentRoot,
                ClientEngine.Godot,
                TransportKind.WebSocket,
                SerializerKind.Json,
                PersistenceKind.None,
                NuGetForUnitySource.OpenUpm,
                DeploymentProfile.None));
            var generator = CreateGenerator();

            var result = await generator.GenerateAsync(spec, TestContext.Current.CancellationToken);

            AssertChatRoomIdContract(spec.Layout.RootPath, expectStartupActors: true);
            AssertChatRoomIdContract(Path.Combine(repositoryRoot, "samples", "Game.Godot.Chat"), expectStartupActors: true);
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
        var hotfixServices = ReadAllTextFiles(Path.Combine(sampleRoot, "Server", "Hotfix", "Services"));
        var clientScripts = ReadAllTextFiles(Path.Combine(sampleRoot, "Client", "Assets", "Scripts"));

        Assert.Contains("node-local actor runtime", readme, StringComparison.Ordinal);
        Assert.Contains("RPC service", readme, StringComparison.Ordinal);
        Assert.Contains("may be local or remote", readme, StringComparison.Ordinal);
        Assert.Contains("typed selector", readme, StringComparison.Ordinal);
        Assert.Contains(".Route(new UserId(", hotfixServices, StringComparison.Ordinal);
        Assert.Contains(".Route(new MatchmakingQueueId(", hotfixServices, StringComparison.Ordinal);
        Assert.Contains(".Local(roomId)", hotfixServices, StringComparison.Ordinal);
        Assert.DoesNotContain(".Remote(new NodeId(", hotfixServices, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Actors", hotfixServices, StringComparison.Ordinal);
        Assert.DoesNotContain("var localActors = call.Actors;", hotfixServices, StringComparison.Ordinal);
        Assert.DoesNotContain(".AskAsync", hotfixServices, StringComparison.Ordinal);
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
        var hotfixServices = ReadAllTextFiles(Path.Combine(sampleRoot, "Server", "Hotfix", "Services"));
        var hotfixState = ReadAllTextFiles(Path.Combine(sampleRoot, "Server", "Hotfix", "State"));
        var hotfixBusinessCode = hotfixServices + hotfixState;
        var sessionDirectoryPath = Path.Combine(
            sampleRoot,
            "Server",
            "Hotfix",
            "Services",
            "PlayerSessionRegistry.cs");
        var registrationPath = Path.Combine(
            sampleRoot,
            "Server",
            "Hotfix",
            "Services",
            "PlayerSessionRegistration.cs");

        Assert.False(File.Exists(sessionDirectoryPath), "Agar hotfix should not own PlayerSessionRegistry.cs.");
        Assert.False(File.Exists(registrationPath), "Agar hotfix should not own PlayerSessionRegistration.cs.");
        Assert.Contains("call.GameServer", hotfixServices, StringComparison.Ordinal);
        Assert.Contains(".StartSessionAsync", hotfixServices, StringComparison.Ordinal);
        Assert.Contains(".ResumeSessionAsync", hotfixServices, StringComparison.Ordinal);
        Assert.Contains(".TerminateSessionAsync", hotfixServices, StringComparison.Ordinal);
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
        var rendererText = ReadAllTextFiles(Path.Combine(repositoryRoot, "src", "Lakona.Tool", "Rendering", "Shared"));
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
        var toolText = ReadAllTextFiles(Path.Combine(repositoryRoot, "src", "Lakona.Tool", "Rendering", "Shared"));

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
            new GenerationExecutor(new TransactionalOutputWriter(ToolText.ForCulture(System.Globalization.CultureInfo.InvariantCulture))),
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
            Assert.Contains("ActorStartupPlan.Create<ChatRoomActor>(ActorId.From(ChatRoomIds.Global))", hotfixChat, StringComparison.Ordinal);
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
        Assert.Contains(".Route(ChatRoomIds.Global)", hotfixChat, StringComparison.Ordinal);
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
