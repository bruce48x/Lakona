using Lakona.Tool.RpcStarter;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Lakona.Tool.Tests;

public sealed class ToolTemplateTests
{
    [Fact]
    public void RenderServerAppSettings_DefaultClusterProject_UsesCompactLakonaGameSection()
    {
        var options = new NewCommandOptions(
            Name: "MyGame",
            OutputPath: null,
            ClientEngine: ProjectConventions.DefaultClientEngine,
            Transport: "kcp",
            NetworkProfile: ProjectConventions.DefaultNetworkProfile,
            Serializer: ProjectConventions.DefaultSerializer,
            Persistence: ProjectConventions.DefaultPersistence,
            NuGetForUnitySource: ProjectConventions.DefaultNuGetForUnitySource,
            DeployProfile: ProjectConventions.DefaultDeployProfile);

        var json = ToolTemplates.RenderServerAppSettings(options);

        Assert.Contains("\"Lakona.Game\"", json);
        Assert.Contains("\"Node\"", json);
        Assert.Contains("\"Id\": \"dev-1\"", json);
        Assert.Contains("\"Endpoints\"", json);
        Assert.Contains("\"Transport\": \"kcp\"", json);
        Assert.Contains("\"Host\": \"127.0.0.1\"", json);
        Assert.Contains("\"Port\": 20000", json);
        Assert.DoesNotContain("\"Endpoint\"", json);
        Assert.DoesNotContain("\"Cluster\"", json);
        Assert.DoesNotContain("\"Deployment\"", json);
        Assert.DoesNotContain("\"Hotfix\"", json);
        Assert.DoesNotContain("\"ReliablePush\"", json);
        Assert.DoesNotContain("\"Bootstrap\"", json);
        Assert.DoesNotContain("\"Services\"", json);
        Assert.DoesNotContain("\"NodeDirectory\"", json);
        Assert.DoesNotContain("\"ControlPlane\"", json);
        Assert.DoesNotContain("\"Realtime\"", json);
    }

    [Fact]
    public void RenderServerAppSettings_WebSocketProject_IncludesEndpointPath()
    {
        var options = new NewCommandOptions(
            Name: "MyGame",
            OutputPath: null,
            ClientEngine: ProjectConventions.DefaultClientEngine,
            Transport: "websocket",
            NetworkProfile: ProjectConventions.DefaultNetworkProfile,
            Serializer: ProjectConventions.DefaultSerializer,
            Persistence: ProjectConventions.DefaultPersistence,
            NuGetForUnitySource: ProjectConventions.DefaultNuGetForUnitySource,
            DeployProfile: ProjectConventions.DefaultDeployProfile);

        var json = ToolTemplates.RenderServerAppSettings(options);

        Assert.Contains("\"Endpoints\"", json);
        Assert.Contains("\"Transport\": \"websocket\"", json);
        Assert.Contains("\"Path\": \"/ws\"", json);
        Assert.DoesNotContain("\"Endpoint\"", json);
        Assert.DoesNotContain("\"AdvertisedEndpoints\"", json);
    }

    [Fact]
    public void RenderServerProgram_DefaultSingleEndpoint_IsThinEntrypoint()
    {
        var options = new NewCommandOptions(
            Name: "MyGame",
            OutputPath: null,
            ClientEngine: ProjectConventions.DefaultClientEngine,
            Transport: "kcp",
            NetworkProfile: ProjectConventions.DefaultNetworkProfile,
            Serializer: ProjectConventions.DefaultSerializer,
            Persistence: ProjectConventions.DefaultPersistence,
            NuGetForUnitySource: ProjectConventions.DefaultNuGetForUnitySource,
            DeployProfile: ProjectConventions.DefaultDeployProfile);

        var source = ToolTemplates.RenderServerProgram(options);

        Assert.Contains("using Server.App.Hosting;", source, StringComparison.Ordinal);
        Assert.Contains("using Lakona.Game.Server.Hosting;", source, StringComparison.Ordinal);
        Assert.Contains("return await LakonaGameServer.RunAsync(args", source, StringComparison.Ordinal);
        Assert.Contains("ServiceBindingConfigurator.Bind", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using Server.Hosting.Advanced;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LakonaGameGeneratedApplication", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LakonaGameRuntimeOptions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ClusterOptions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddRpcServer", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderServerProgram_RealtimeProfile_IsThinEntrypoint()
    {
        var options = new NewCommandOptions(
            Name: "MyGame",
            OutputPath: null,
            ClientEngine: ProjectConventions.DefaultClientEngine,
            Transport: "websocket",
            NetworkProfile: "realtime",
            Serializer: ProjectConventions.DefaultSerializer,
            Persistence: ProjectConventions.DefaultPersistence,
            NuGetForUnitySource: ProjectConventions.DefaultNuGetForUnitySource,
            DeployProfile: ProjectConventions.DefaultDeployProfile);

        var source = ToolTemplates.RenderServerProgram(options);

        Assert.Contains("using Server.App.Hosting;", source, StringComparison.Ordinal);
        Assert.Contains("using Lakona.Game.Server.Hosting;", source, StringComparison.Ordinal);
        Assert.Contains("return await LakonaGameServer.RunAsync(args", source, StringComparison.Ordinal);
        Assert.Contains("ServiceBindingConfigurator.Bind", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using Server.Hosting.Advanced;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LakonaGameGeneratedApplication", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LakonaGameRuntimeOptions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ClusterOptions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddRpcServer", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderServerProgram_AcceptorFactory_PassesHost()
    {
        var wsSource = ToolTemplates.RenderServerProgram(new NewCommandOptions(
            Name: "MyGame",
            OutputPath: null,
            ClientEngine: "godot",
            Transport: "websocket",
            NetworkProfile: "cluster",
            Serializer: "json",
            Persistence: "none",
            NuGetForUnitySource: "embedded",
            DeployProfile: "none"));

        var tcpSource = ToolTemplates.RenderServerProgram(new NewCommandOptions(
            Name: "MyGame",
            OutputPath: null,
            ClientEngine: "godot",
            Transport: "tcp",
            NetworkProfile: "cluster",
            Serializer: "json",
            Persistence: "none",
            NuGetForUnitySource: "embedded",
            DeployProfile: "none"));

        var kcpSource = ToolTemplates.RenderServerProgram(new NewCommandOptions(
            Name: "MyGame",
            OutputPath: null,
            ClientEngine: "godot",
            Transport: "kcp",
            NetworkProfile: "cluster",
            Serializer: "json",
            Persistence: "none",
            NuGetForUnitySource: "embedded",
            DeployProfile: "none"));

        // All three transports must pass opts.Host to the acceptor
        Assert.Contains("opts.Host", wsSource, StringComparison.Ordinal);
        Assert.Contains("opts.Host", tcpSource, StringComparison.Ordinal);
        Assert.Contains("opts.Host", kcpSource, StringComparison.Ordinal);

        // No hardcoded 0.0.0.0 in acceptor construction
        Assert.DoesNotContain("0.0.0.0", wsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("0.0.0.0", tcpSource, StringComparison.Ordinal);
        Assert.DoesNotContain("0.0.0.0", kcpSource, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderServerProgram_TcpProject_UsesSelectedTransport()
    {
        var options = new NewCommandOptions(
            Name: "MyGame",
            OutputPath: null,
            ClientEngine: "unity",
            Transport: "tcp",
            NetworkProfile: "single",
            Serializer: "json",
            Persistence: "none",
            NuGetForUnitySource: ProjectConventions.DefaultNuGetForUnitySource,
            DeployProfile: ProjectConventions.DefaultDeployProfile);

        var source = ToolTemplates.RenderServerProgram(options);

        Assert.Contains(".UseTransport(\"tcp\")", source, StringComparison.Ordinal);
        Assert.Contains(".UseAcceptor(async opts => new TcpConnectionAcceptor(opts.Port, opts.Host))", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".UseTransport(\"websocket\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderServerProgram_KcpProject_UsesSelectedTransport()
    {
        var options = new NewCommandOptions(
            Name: "MyGame",
            OutputPath: null,
            ClientEngine: "unity",
            Transport: "kcp",
            NetworkProfile: "single",
            Serializer: ProjectConventions.DefaultSerializer,
            Persistence: ProjectConventions.DefaultPersistence,
            NuGetForUnitySource: ProjectConventions.DefaultNuGetForUnitySource,
            DeployProfile: ProjectConventions.DefaultDeployProfile);

        var source = ToolTemplates.RenderServerProgram(options);

        Assert.Contains(".UseTransport(\"kcp\")", source, StringComparison.Ordinal);
        Assert.Contains(".UseAcceptor(async opts => new KcpConnectionAcceptor(opts.Port, opts.Host, 100))", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderGeneratedServerApplication_RealtimeProfile_ReturnsRealtimeGeneratedApplication()
    {
        var options = new NewCommandOptions(
            Name: "MyGame",
            OutputPath: null,
            ClientEngine: ProjectConventions.DefaultClientEngine,
            Transport: "websocket",
            NetworkProfile: "realtime",
            Serializer: ProjectConventions.DefaultSerializer,
            Persistence: ProjectConventions.DefaultPersistence,
            NuGetForUnitySource: ProjectConventions.DefaultNuGetForUnitySource,
            DeployProfile: ProjectConventions.DefaultDeployProfile);

        var source = ToolTemplates.RenderGeneratedServerApplication(options);

        Assert.Contains("LakonaGameGeneratedApplication", source, StringComparison.Ordinal);
        Assert.Contains("LakonaGameServer.RunAsync", source, StringComparison.Ordinal);
        Assert.Contains("ServiceBindingConfigurator.Bind", source, StringComparison.Ordinal);
        Assert.Contains("AddRpcEndpoint", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LakonaGameRuntimeOptions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("runtimeOptions.ToServerRpcServerOptions", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultScaffoldIncludesServerHotfixInfrastructure()
    {
        var options = CliParser.ParseNewOptions([]);

        var solution = ToolTemplates.RenderServerSolution();
        var project = ToolTemplates.RenderServerProject(options);
        var sharedContractIds = ToolTemplates.RenderSharedRpcContractIds();
        var sharedProtocols = ToolTemplates.RenderSharedChatProtocols();
        var sharedMessages = ToolTemplates.RenderSharedChatMessages();
        var hotfixProject = ToolTemplates.RenderHotfixProject();
        var appSettings = ToolTemplates.RenderServerAppSettings(options);
        var program = ToolTemplates.RenderServerProgram(options);
        var generatedApplication = ToolTemplates.RenderGeneratedServerApplication(options);
        var chatRoomActor = ToolTemplates.RenderServerChatRoomActor();
        var chatService = ToolTemplates.RenderHotfixChatService();
        var generatedText = string.Concat(
            solution,
            project,
            sharedContractIds,
            sharedProtocols,
            sharedMessages,
            hotfixProject,
            appSettings,
            program,
            generatedApplication,
            chatRoomActor,
            chatService);

        Assert.Contains(@"<Project Path=""Hotfix/Server.Hotfix.csproj"" />", solution, StringComparison.Ordinal);
        Assert.DoesNotContain(@"<ProjectReference Include=""..\Hotfix\Server.Hotfix.csproj""", project, StringComparison.Ordinal);
        Assert.Contains(@"PackageReference Include=""Lakona.Game.Server.Hotfix""", project, StringComparison.Ordinal);
        Assert.Contains(@"PackageReference Include=""Lakona.Game.Server.Generators""", project, StringComparison.Ordinal);
        Assert.Contains(@"PrivateAssets=""all"" OutputItemType=""Analyzer""", project, StringComparison.Ordinal);
        Assert.Contains("public const int Chat = 2;", sharedContractIds, StringComparison.Ordinal);
        Assert.Contains("public const int MessageReceived = 1;", sharedContractIds, StringComparison.Ordinal);
        Assert.Contains("[RpcService(RpcContractIds.Services.Chat, NotificationContract = typeof(IChatCallback))]", sharedProtocols, StringComparison.Ordinal);
        Assert.Contains("interface IChatService", sharedProtocols, StringComparison.Ordinal);
        Assert.Contains("interface IChatCallback", sharedProtocols, StringComparison.Ordinal);
        Assert.Contains("LoginRequest", sharedMessages, StringComparison.Ordinal);
        Assert.Contains("ChatMessage", sharedMessages, StringComparison.Ordinal);
        Assert.Contains(@"ProjectReference Include=""..\..\Shared\Shared.csproj""", hotfixProject, StringComparison.Ordinal);
        Assert.Contains(@"ProjectReference Include=""..\App\Server.App.csproj""", hotfixProject, StringComparison.Ordinal);
        Assert.Contains("class ChatRoomActor : Actor", chatRoomActor, StringComparison.Ordinal);
        Assert.Contains("IActorRuntime", chatService, StringComparison.Ordinal);
        Assert.Contains("class ChatService", chatService, StringComparison.Ordinal);
        Assert.Contains("IChatService", chatService, StringComparison.Ordinal);
        Assert.Contains("FilterMessage", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("static readonly ChatRoom", generatedText, StringComparison.Ordinal);
        Assert.DoesNotContain("AddLakonaGameHotfix", program, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentDirectoryHotfixAssemblySource", program, StringComparison.Ordinal);
        Assert.DoesNotContain("IHotfixManager", program, StringComparison.Ordinal);
        Assert.Empty(generatedApplication);
        Assert.DoesNotContain("Agar.Sample.Hotfix", generatedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AugmentExistingStarterServerProjectUsesFluentRunAsync()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "lakona-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var serverDirectory = Path.Combine(projectRoot, "Server", "App");
            Directory.CreateDirectory(serverDirectory);
            Directory.CreateDirectory(Path.Combine(projectRoot, "Shared"));
            await File.WriteAllTextAsync(
                Path.Combine(serverDirectory, "Server.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """,
                TestContext.Current.CancellationToken);

            await new ProjectScaffolder().AugmentProjectWithLakonaGameAsync(projectRoot, CliParser.ParseNewOptions([]));

            var program = await File.ReadAllTextAsync(
                Path.Combine(serverDirectory, "Program.cs"),
                TestContext.Current.CancellationToken);
            var serviceBindingConfigurator = await File.ReadAllTextAsync(
                Path.Combine(serverDirectory, "Hosting", "ServiceBindingConfigurator.cs"),
                TestContext.Current.CancellationToken);

            Assert.Contains("return await LakonaGameServer.RunAsync(args", program, StringComparison.Ordinal);
            Assert.Contains("ServiceBindingConfigurator.Bind", program, StringComparison.Ordinal);
            Assert.DoesNotContain("LakonaGameGeneratedApplication", program, StringComparison.Ordinal);
            Assert.Contains("class ServiceBindingConfigurator", serviceBindingConfigurator, StringComparison.Ordinal);
            Assert.Contains("IServiceProvider services", serviceBindingConfigurator, StringComparison.Ordinal);
            Assert.DoesNotContain("PingServiceBinder.BindFactory", serviceBindingConfigurator, StringComparison.Ordinal);
            Assert.DoesNotContain("Server.Hotfix.Services.PingService", serviceBindingConfigurator, StringComparison.Ordinal);
            Assert.Contains("ChatServiceBinder.Bind", serviceBindingConfigurator, StringComparison.Ordinal);
            Assert.Contains("""LoadHotfixType("Server.Hotfix.Chat.ChatService")""", serviceBindingConfigurator, StringComparison.Ordinal);
        Assert.Contains("""LoadHotfixType("Server.Hotfix.Login.LoginService")""", serviceBindingConfigurator, StringComparison.Ordinal);
            Assert.DoesNotContain("AllServicesBinder.BindAll", serviceBindingConfigurator, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(serverDirectory, "Hosting", "Advanced", "LakonaGameGeneratedApplication.cs")));
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AugmentExistingStarterServerProjectAddsHotfixCopyTarget()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "lakona-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var serverDirectory = Path.Combine(projectRoot, "Server", "App");
            Directory.CreateDirectory(serverDirectory);
            Directory.CreateDirectory(Path.Combine(projectRoot, "Shared"));
            await File.WriteAllTextAsync(
                Path.Combine(serverDirectory, "Server.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """,
                TestContext.Current.CancellationToken);

            await new ProjectScaffolder().AugmentProjectWithLakonaGameAsync(projectRoot, CliParser.ParseNewOptions([]));

            var hotfixProject = await File.ReadAllTextAsync(
                Path.Combine(projectRoot, "Server", "Hotfix", "Server.Hotfix.csproj"),
                TestContext.Current.CancellationToken);

            Assert.Contains(@"<Target Name=""CopyHotfixOutput"" AfterTargets=""Build"">", hotfixProject, StringComparison.Ordinal);
            Assert.Contains(@"DestinationFolder=""$(ProjectDir)..\App\bin\$(Configuration)\$(TargetFramework)\hotfix\""", hotfixProject, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void RenderSharedChatProtocols_DefinesRpcServiceAndCallbackWithNamedContractIds()
    {
        var source = ToolTemplates.RenderSharedChatProtocols();

        Assert.Contains("[RpcService(RpcContractIds.Services.Chat, NotificationContract = typeof(IChatCallback))]", source, StringComparison.Ordinal);
        Assert.Contains("[RpcMethod(RpcContractIds.ChatServiceMethods.BindAsync)]", source, StringComparison.Ordinal);
        Assert.Contains("ValueTask BindAsync(ChatBindRequest req);", source, StringComparison.Ordinal);
        Assert.Contains("[RpcMethod(RpcContractIds.ChatServiceMethods.SendAsync)]", source, StringComparison.Ordinal);
        Assert.Contains("[RpcNotification(RpcContractIds.ChatNotifications.MessageReceived)]", source, StringComparison.Ordinal);
        Assert.Contains("OnMessageReceived", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JoinAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LeaveAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OnUserJoined", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OnUserLeft", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[RpcService(2", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[RpcMethod(1)]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[RpcNotification(1)]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderSharedRpcContractIds_DefinesChatIds()
    {
        var source = ToolTemplates.RenderSharedRpcContractIds();

        Assert.Contains("namespace Shared.Contracts", source, StringComparison.Ordinal);
        Assert.Contains("public const int Login = 1;", source, StringComparison.Ordinal);
        Assert.Contains("public const int Chat = 2;", source, StringComparison.Ordinal);
        Assert.Contains("public const int LoginAsync = 1;", source, StringComparison.Ordinal);
        Assert.Contains("public const int BindAsync = 1;", source, StringComparison.Ordinal);
        Assert.Contains("public const int SendAsync = 2;", source, StringComparison.Ordinal);
        Assert.Contains("public const int MessageReceived = 1;", source, StringComparison.Ordinal);
        Assert.Contains("public const int UserJoined = 1;", source, StringComparison.Ordinal);
        Assert.Contains("public const int UserLeft = 2;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderSharedChatMessages_DoesNotExposeConnectionId()
    {
        var source = ToolTemplates.RenderSharedChatMessages();

        Assert.DoesNotContain("ConnectionId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public string ConnectionId", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderSharedChatMessages_UsesSelectedSerializerAnnotations()
    {
        var jsonOptions = new NewCommandOptions(
            Name: "MyGame",
            OutputPath: null,
            ClientEngine: "unity",
            Transport: "websocket",
            NetworkProfile: "single",
            Serializer: "json",
            Persistence: "none",
            NuGetForUnitySource: "embedded",
            DeployProfile: "none");

        var jsonSource = ToolTemplates.RenderSharedChatMessages(jsonOptions);
        var memoryPackSource = ToolTemplates.RenderSharedChatMessages();

        Assert.DoesNotContain("MemoryPack", jsonSource, StringComparison.Ordinal);
        Assert.Contains("public partial class ChatMessage", jsonSource, StringComparison.Ordinal);
        Assert.Contains("using MemoryPack;", memoryPackSource, StringComparison.Ordinal);
        Assert.Contains("[MemoryPackable(GenerateType.VersionTolerant)]", memoryPackSource, StringComparison.Ordinal);
        Assert.Contains("[MemoryPackOrder(2)] public long Timestamp", memoryPackSource, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderServerHostingTemplates_ParseAsCurrentCSharp()
    {
        var options = CliParser.ParseNewOptions([]);
        var realtimeOptions = new NewCommandOptions(
            Name: "MyGame",
            OutputPath: null,
            ClientEngine: ProjectConventions.DefaultClientEngine,
            Transport: "websocket",
            NetworkProfile: "realtime",
            Serializer: ProjectConventions.DefaultSerializer,
            Persistence: ProjectConventions.DefaultPersistence,
            NuGetForUnitySource: ProjectConventions.DefaultNuGetForUnitySource,
            DeployProfile: ProjectConventions.DefaultDeployProfile);
        var sources = new[]
        {
            ("Server/App/Program.cs", ToolTemplates.RenderServerProgram(options)),
            ("Server/App/Hosting/Advanced/LakonaGameGeneratedApplication.cs", ToolTemplates.RenderGeneratedServerApplication(options)),
            ("Server/App/Hosting/ServiceBindingConfigurator.cs", ToolTemplates.RenderServiceBindingConfigurator()),
            ("Server/App/RealtimeProgram.cs", ToolTemplates.RenderServerProgram(realtimeOptions)),
            ("Server/App/Hosting/Advanced/RealtimeLakonaGameGeneratedApplication.cs", ToolTemplates.RenderGeneratedServerApplication(realtimeOptions)),
        };

        AssertGeneratedSourcesParseAsCurrentCSharp(sources);
    }

    [Fact]
    public void RenderUnityFacingChatTemplates_ParseAsCSharpNine()
    {
        var sources = new[]
        {
            ("Shared/Contracts/RpcContractIds.cs", ToolTemplates.RenderSharedRpcContractIds()),
            ("Shared/Contracts/Chat/ChatProtocols.cs", ToolTemplates.RenderSharedChatProtocols()),
            ("Shared/Contracts/Chat/ChatMessages.cs", ToolTemplates.RenderSharedChatMessages()),
            ("Client/Assets/Scripts/Login/LoginClient.cs", ToolTemplates.RenderClientLoginClient()),
            ("Client/Assets/Scripts/Chat/ChatClient.cs", ToolTemplates.RenderClientChatClient()),
            ("Client/Assets/Scripts/Chat/ChatUI.cs", ToolTemplates.RenderClientChatUI())
        };

        AssertGeneratedSourcesParseAsCSharp9(sources);
    }

    [Fact]
    public void RenderServerChatTemplates_ParseAsCurrentCSharp()
    {
        var sources = new (string, string)[]
        {
            ("Server/App/Chat/ChatRoomActor.cs", ToolTemplates.RenderServerChatRoomActor()),
            ("Server/App/Chat/ChatConnectionLifecycle.cs", ToolTemplates.RenderServerChatConnectionLifecycle()),
            ("Server/Hotfix/Chat/ChatService.cs", ToolTemplates.RenderHotfixChatService()),
            ("Server/Hotfix/Login/LoginService.cs", ToolTemplates.RenderHotfixLoginService()),
        };

        AssertGeneratedSourcesParseAsCurrentCSharp(sources);
    }

    private static void AssertGeneratedSourcesParseAsCSharp9(IEnumerable<(string Path, string Source)> sources)
    {
        AssertGeneratedSourcesParse(sources, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9));
    }

    private static void AssertGeneratedSourcesParseAsCurrentCSharp(IEnumerable<(string Path, string Source)> sources)
    {
        AssertGeneratedSourcesParse(sources, CSharpParseOptions.Default);
    }

    private static void AssertGeneratedSourcesParse(IEnumerable<(string Path, string Source)> sources, CSharpParseOptions parseOptions)
    {
        var diagnostics = new List<string>();

        foreach (var (path, source) in sources)
        {
            var tree = CSharpSyntaxTree.ParseText(source, parseOptions, path);
            diagnostics.AddRange(
                tree.GetDiagnostics()
                    .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => $"{path}: {diagnostic.Id} {diagnostic.GetMessage()}"));
        }

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void RenderServerChatRoomActor_UsesActorRuntimeStateModel()
    {
        var source = ToolTemplates.RenderServerChatRoomActor();

        Assert.Contains("class ChatRoomActor : Actor", source, StringComparison.Ordinal);
        Assert.Contains("using Lakona.Game.Server.Actors;", source, StringComparison.Ordinal);
        Assert.Contains("private readonly Dictionary<string,", source, StringComparison.Ordinal);
        Assert.Contains("private readonly Queue<ChatMessage>", source, StringComparison.Ordinal);
        Assert.Contains("BroadcastChat(cb => cb.OnMessageReceived", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConcurrentDictionary", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConcurrentQueue", source, StringComparison.Ordinal);
        Assert.DoesNotContain("lock (", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderHotfixChatService_UsesActorRuntime()
    {
        var source = ToolTemplates.RenderHotfixChatService();

        Assert.Contains("class ChatService : IChatService", source, StringComparison.Ordinal);
        Assert.Contains("private readonly IActorRuntime _actors;", source, StringComparison.Ordinal);
        Assert.Contains("private readonly string _connectionId;", source, StringComparison.Ordinal);
        Assert.Contains("public ChatService(IChatCallback callback, IActorRuntime actors, string connectionId)", source, StringComparison.Ordinal);
        Assert.Contains("await BindAsync(new ChatBindRequest());", source, StringComparison.Ordinal);
        Assert.Contains("_connectionId", source, StringComparison.Ordinal);
        Assert.Contains("ActorId.From(\"chat:global\")", source, StringComparison.Ordinal);
        Assert.Contains("_actors.AskAsync<ChatRoomActor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("req.ConnectionId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("static readonly ChatRoom", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new ChatRoom", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderHotfixLoginService_UsesInjectedConnectionId()
    {
        var source = ToolTemplates.RenderHotfixLoginService();

        Assert.Contains("public LoginService(ILoginCallback callback, IActorRuntime actors, string connectionId)", source, StringComparison.Ordinal);
        Assert.Contains("_connectionId = connectionId;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Guid.NewGuid()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderHotfixChatService_BindsCallbackBeforeSending()
    {
        var source = ToolTemplates.RenderHotfixChatService();

        Assert.Contains("public ChatService(IChatCallback callback, IActorRuntime actors, string connectionId)", source, StringComparison.Ordinal);
        Assert.Contains("public async ValueTask BindAsync(ChatBindRequest req)", source, StringComparison.Ordinal);
        Assert.Contains("room.BindChatCallback(_connectionId, _callback);", source, StringComparison.Ordinal);
        Assert.Contains("await BindAsync(new ChatBindRequest());", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Guid.NewGuid()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderHotfixChatService_DoesNotTrustClientSuppliedConnectionId()
    {
        var source = ToolTemplates.RenderHotfixChatService();

        Assert.DoesNotContain("req.ConnectionId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatSendRequest { Text = text, ConnectionId", source, StringComparison.Ordinal);
        Assert.Contains("_connectionId", source, StringComparison.Ordinal);
        Assert.Contains("room.BindChatCallback(_connectionId, _callback);", source, StringComparison.Ordinal);
        Assert.Contains("await room.SendAsync(_connectionId, text);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderServiceBindingConfigurator_UsesDependencyInjectionForNotificationServices()
    {
        var source = ToolTemplates.RenderServiceBindingConfigurator();

        Assert.DoesNotContain("using Shared.Interfaces;", source, StringComparison.Ordinal);
        Assert.Contains("using Shared.Contracts.Chat;", source, StringComparison.Ordinal);
        Assert.Contains("using Server.App.Generated;", source, StringComparison.Ordinal);
        Assert.Contains("public static void Bind(RpcServiceRegistry registry, IServiceProvider services)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PingServiceBinder.BindFactory", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Server.Hotfix.Services.PingService", source, StringComparison.Ordinal);
        Assert.Contains("LoginServiceBinder.Bind", source, StringComparison.Ordinal);
        Assert.Contains("ChatServiceBinder.Bind", source, StringComparison.Ordinal);
        Assert.Contains("""LoadHotfixType("Server.Hotfix.Chat.ChatService")""", source, StringComparison.Ordinal);
        Assert.Contains("""LoadHotfixType("Server.Hotfix.Login.LoginService")""", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AllServicesBinder.BindAll", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderServiceBindingConfigurator_UsesRpcSessionContextForLoginAndChatServices()
    {
        var source = ToolTemplates.RenderServiceBindingConfigurator();

        Assert.Contains("using Lakona.Rpc.Server;", source, StringComparison.Ordinal);
        Assert.Contains("using Server.App.Chat;", source, StringComparison.Ordinal);
        Assert.Contains("LoginServiceBinder.BindFactory", source, StringComparison.Ordinal);
        Assert.Contains("ChatServiceBinder.BindFactory", source, StringComparison.Ordinal);
        Assert.Contains("new LoginCallbackProxy(session)", source, StringComparison.Ordinal);
        Assert.Contains("new ChatCallbackProxy(session)", source, StringComparison.Ordinal);
        Assert.Contains("session.ContextId", source, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<ChatConnectionLifecycle>().Track(session)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("callback =>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderServerChatConnectionLifecycle_ObservesDisconnectCleanupTask()
    {
        var source = ToolTemplates.RenderServerChatConnectionLifecycle();

        Assert.Contains("_ = LeaveAsync(session.ContextId);", source, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", source, StringComparison.Ordinal);
        Assert.Contains("Console.Error.WriteLine", source, StringComparison.Ordinal);
        Assert.DoesNotContain("session.Disconnected += _ => LeaveAsync(session.ContextId);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderClientChatClient_WrapsLoginClientNotRpcClient()
    {
        var source = ToolTemplates.RenderClientChatClient();

        Assert.Contains("class ChatClient", source, StringComparison.Ordinal);
        Assert.Contains("using System.Threading.Tasks;", source, StringComparison.Ordinal);
        Assert.Contains("using Client.Login;", source, StringComparison.Ordinal);
        Assert.Contains("private readonly LoginClient _loginClient;", source, StringComparison.Ordinal);
        Assert.Contains("loginClient.RpcClient.Api.Shared.Chat", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using Rpc.Generated;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using Lakona.Rpc.Client;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JoinAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LeaveAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderClientLoginClient_RegistersLoginAndChatCallbacksBeforeConnect()
    {
        var source = ToolTemplates.RenderClientLoginClient();

        Assert.Contains("public sealed class LoginClient : ILoginCallback, IChatCallback, IAsyncDisposable", source, StringComparison.Ordinal);
        Assert.Contains("callbacks.Add((ILoginCallback)this);", source, StringComparison.Ordinal);
        Assert.Contains("callbacks.Add((IChatCallback)this);", source, StringComparison.Ordinal);
        Assert.Contains("public event Action<ChatMessage>? OnMessageReceived;", source, StringComparison.Ordinal);
        Assert.Contains("void IChatCallback.OnMessageReceived(ChatMessage msg)", source, StringComparison.Ordinal);
        Assert.Contains("OnMessageReceived?.Invoke(msg);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderClientChatClient_UsesExistingLoginClientAndBindAsync()
    {
        var source = ToolTemplates.RenderClientChatClient();

        Assert.Contains("private readonly LoginClient _loginClient;", source, StringComparison.Ordinal);
        Assert.Contains("public ChatClient(LoginClient loginClient)", source, StringComparison.Ordinal);
        Assert.Contains("_chatService = loginClient.RpcClient.Api.Shared.Chat;", source, StringComparison.Ordinal);
        Assert.Contains("public async Task BindAsync()", source, StringComparison.Ordinal);
        Assert.Contains("await _chatService.BindAsync(new ChatBindRequest());", source, StringComparison.Ordinal);
        Assert.Contains("public event Action<ChatMessage>? OnMessageReceived", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new RpcClient.RpcNotificationBindings()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("callbacks.Add(this)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderChatTemplatesUseSharedContractsChatNamespace()
    {
        var protocols = ToolTemplates.RenderSharedChatProtocols();
        var messages = ToolTemplates.RenderSharedChatMessages();
        var roomActor = ToolTemplates.RenderServerChatRoomActor();
        var service = ToolTemplates.RenderHotfixChatService();
        var client = ToolTemplates.RenderClientChatClient();
        var unityUi = ToolTemplates.RenderClientChatUI();
        var godotScene = ToolTemplates.RenderGodotChatScene();

        Assert.Contains("namespace Shared.Contracts.Chat", protocols, StringComparison.Ordinal);
        Assert.Contains("namespace Shared.Contracts.Chat", messages, StringComparison.Ordinal);
        Assert.Contains("using Shared.Contracts.Chat;", roomActor, StringComparison.Ordinal);
        Assert.Contains("using Shared.Contracts.Chat;", service, StringComparison.Ordinal);
        Assert.Contains("using Server.App.Chat;", service, StringComparison.Ordinal);
        Assert.Contains("using Shared.Contracts.Chat;", client, StringComparison.Ordinal);
        Assert.Contains("using Shared.Contracts.Chat;", unityUi, StringComparison.Ordinal);
        Assert.Contains("using Shared.Contracts.Chat;", godotScene, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace Shared.Chat", string.Concat(protocols, messages, roomActor, service, client, unityUi, godotScene), StringComparison.Ordinal);
        Assert.DoesNotContain("using Shared.Chat;", string.Concat(protocols, messages, roomActor, service, client, unityUi, godotScene), StringComparison.Ordinal);
    }

    [Fact]
    public void RenderClientChatUi_RequiresUiDocument()
    {
        var source = ToolTemplates.RenderClientChatUI();

        Assert.Contains("RequireComponent(typeof(UIDocument))", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new KcpTransport(_serverHost, _serverPort)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new MemoryPackRpcSerializer()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("?.clicked +=", source, StringComparison.Ordinal);
        Assert.Contains("ConcurrentQueue<Action>", source, StringComparison.Ordinal);
        Assert.Contains("client.OnMessageReceived += msg => EnqueueMainThread(() => AppendMessage(msg));", source, StringComparison.Ordinal);
        Assert.Contains("AppendSystemMessage(\"Not connected.\");", source, StringComparison.Ordinal);
        Assert.Contains("chat-input", source, StringComparison.Ordinal);
        Assert.Contains("message-list", source, StringComparison.Ordinal);
        Assert.Contains("send-button", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderClientChatUI_ImportsLoginNamespace()
    {
        var source = ToolTemplates.RenderClientChatUI();

        Assert.Contains("using Client.Login;", source, StringComparison.Ordinal);
        Assert.Contains("private LoginClient? _loginClient;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderGodotChatScene_ImportsLoginNamespace()
    {
        var source = ToolTemplates.RenderGodotChatScene();

        Assert.Contains("using Client.Login;", source, StringComparison.Ordinal);
        Assert.Contains("private LoginClient? _loginClient;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderClientChatTemplates_DoNotUseSessionConnectionId()
    {
        var chatClient = ToolTemplates.RenderClientChatClient();
        var unityUi = ToolTemplates.RenderClientChatUI();
        var godotScene = ToolTemplates.RenderGodotChatScene();
        var session = ToolTemplates.RenderChatSession();
        var godotSession = ToolTemplates.RenderGodotChatSession();

        var combined = string.Concat(chatClient, unityUi, godotScene, session, godotSession);

        Assert.DoesNotContain("session.ConnectionId", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatSession.ConnectionId", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectionId", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderGodotLoginScene_DoesNotUseSessionConnectionId()
    {
        var source = ToolTemplates.RenderGodotLoginScene(new NewCommandOptions(
            Name: "MyGame",
            OutputPath: null,
            ClientEngine: "godot",
            Transport: "websocket",
            NetworkProfile: "cluster",
            Serializer: "json",
            Persistence: "none",
            NuGetForUnitySource: "embedded",
            DeployProfile: "none"));

        Assert.Contains("session.LoginClient = client;", source, StringComparison.Ordinal);
        Assert.Contains("session.LoginReply = reply;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("session.ConnectionId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("reply.ConnectionId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectionId", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderUnityLoginUi_UsesSelectedTransportAndSerializer()
    {
        var source = ToolTemplates.RenderUnityLoginUI(new NewCommandOptions(
            Name: "MyGame",
            OutputPath: null,
            ClientEngine: "unity",
            Transport: "websocket",
            NetworkProfile: "cluster",
            Serializer: "json",
            Persistence: "none",
            NuGetForUnitySource: "embedded",
            DeployProfile: "none"));

        Assert.Contains("using Lakona.Rpc.Transport.WebSocket;", source, StringComparison.Ordinal);
        Assert.Contains("using Lakona.Rpc.Serializer.Json;", source, StringComparison.Ordinal);
        Assert.Contains("new WsTransport($\"ws://{_serverHost}:{_serverPort}{NormalizePath(_serverPath)}\")", source, StringComparison.Ordinal);
        Assert.Contains("new JsonRpcSerializer()", source, StringComparison.Ordinal);
        Assert.Contains("[SerializeField] private string _serverPath = \"/ws\";", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderClientChatUss_UsesReadableDarkThemeControls()
    {
        var source = ToolTemplates.RenderClientChatUss();

        Assert.Contains("var(--lakona-text-body)", source, StringComparison.Ordinal);
        Assert.Contains("var(--lakona-accent)", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".join-button", source, StringComparison.Ordinal);
        Assert.Contains(".send-button:disabled", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderUnityChatSceneObjects_WiresUiDocumentAndChatUi()
    {
        var source = ToolTemplates.RenderUnityChatSceneObjects(
            100,
            101,
            102,
            103,
            "chatuiscriptguid",
            "uxmlguid",
            "panelsettingsguid");

        Assert.Contains("m_Name: Lakona.Game Chat UI", source, StringComparison.Ordinal);
        Assert.Contains("m_Script: {fileID: 11500000, guid: chatuiscriptguid, type: 3}", source, StringComparison.Ordinal);
        Assert.Contains("m_Script: {fileID: 19102, guid: 0000000000000000e000000000000000, type: 0}", source, StringComparison.Ordinal);
        Assert.Contains("m_PanelSettings: {fileID: 11400000, guid: panelsettingsguid, type: 2}", source, StringComparison.Ordinal);
        Assert.Contains("sourceAsset: {fileID: 9197481963319205126, guid: uxmlguid, type: 3}", source, StringComparison.Ordinal);
        Assert.Contains("_serverPath: ", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_serverPath: /ws", source, StringComparison.Ordinal);
        Assert.Contains("--- !u!4 &103", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderUnityPanelSettingsAsset_UsesPanelSettingsScript()
    {
        var source = ToolTemplates.RenderUnityPanelSettingsAsset("themeguid");

        Assert.Contains("m_Script: {fileID: 19101, guid: 0000000000000000e000000000000000, type: 0}", source, StringComparison.Ordinal);
        Assert.Contains("m_Name: LakonaGameChatPanelSettings", source, StringComparison.Ordinal);
        Assert.Contains("themeUss: {fileID: -4733365628477956816, guid: themeguid, type: 3}", source, StringComparison.Ordinal);
        Assert.Contains("m_ReferenceResolution: {x: 1200, y: 800}", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderUnityNuGetPackageImportGuard_ScansExistingAnalyzerPlugins()
    {
        var source = ToolTemplates.RenderUnityNuGetPackageImportGuard();

        Assert.Contains("[InitializeOnLoad]", source, StringComparison.Ordinal);
        Assert.Contains("AssetDatabase.FindAssets(\"t:PluginImporter\", new[] { \"Assets/Packages\" })", source, StringComparison.Ordinal);
        Assert.Contains("AssetDatabase.GUIDToAssetPath", source, StringComparison.Ordinal);
        Assert.Contains("EditorApplication.delayCall += DisableExistingAnalyzerPlugins", source, StringComparison.Ordinal);
        Assert.Contains("/analyzers/", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderClientChatUxml_UsesUiNamespacePrefix()
    {
        var source = ToolTemplates.RenderClientChatUxml();

        Assert.Contains("<ui:UXML", source, StringComparison.Ordinal);
        Assert.Contains("<Style src=\"ChatScene.uss\" />", source, StringComparison.Ordinal);
        Assert.Contains("name=\"chat-input\"", source, StringComparison.Ordinal);
        Assert.Contains("name=\"message-list\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderUnityDefaultRuntimeTheme_ImportsUnityDefaultTheme()
    {
        var source = ToolTemplates.RenderUnityDefaultRuntimeTheme();

        Assert.Contains("@import url(\"unity-theme://default\");", source, StringComparison.Ordinal);
        Assert.Contains("--lakona-bg-base: #0A0F0A;", source, StringComparison.Ordinal);
        Assert.Contains("--lakona-accent: #00FF66;", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AugmentExistingStarterServerProjectKeepsStarterPingSampleContracts()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "lakona-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var serverDirectory = Path.Combine(projectRoot, "Server", "App");
            var interfacesDirectory = Path.Combine(projectRoot, "Shared", "Interfaces");
            Directory.CreateDirectory(serverDirectory);
            Directory.CreateDirectory(interfacesDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(serverDirectory, "Server.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """,
                TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(interfacesDirectory, "IPingService.cs"),
                """
                using System.Threading.Tasks;
                using Lakona.Rpc.Core;

                namespace Shared.Interfaces
                {
                    [RpcService(RpcContractIds.Services.Ping)]
                    public interface IPingService
                    {
                        [RpcMethod(RpcContractIds.PingServiceMethods.PingAsync)]
                        ValueTask<PingReply> PingAsync(PingRequest request);
                    }
                }
                """,
                TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(interfacesDirectory, "SharedDtos.cs"),
                """
                namespace Shared.Interfaces
                {
                    public sealed class PingRequest
                    {
                        public string Message { get; set; } = "";
                    }

                    public sealed class PingReply
                    {
                        public string Message { get; set; } = "";
                    }
                }
                """,
                TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(interfacesDirectory, "RpcContractIds.cs"),
                """
                namespace Shared.Interfaces
                {
                    public static class RpcContractIds
                    {
                        public static class Services
                        {
                            public const int Ping = 1;
                        }

                        public static class PingServiceMethods
                        {
                            public const int PingAsync = 1;
                        }
                    }
                }
                """,
                TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(interfacesDirectory, "IPingService.cs.meta"), "fileFormatVersion: 2", TestContext.Current.CancellationToken);
            Directory.CreateDirectory(Path.Combine(serverDirectory, "Services"));
            await File.WriteAllTextAsync(
                Path.Combine(serverDirectory, "Services", "PingService.cs"),
                """
                using Shared.Interfaces;

                namespace Server.Services
                {
                    public sealed class PingService : IPingService
                    {
                        public ValueTask<PingReply> PingAsync(PingRequest request)
                        {
                            return ValueTask.FromResult(new PingReply
                            {
                                Message = string.IsNullOrWhiteSpace(request.Message) ? "pong" : "pong: " + request.Message,
                                ServerTimeUtc = DateTime.UtcNow.ToString("O")
                            });
                        }
                    }
                }
                """,
                TestContext.Current.CancellationToken);

            await new ProjectScaffolder().AugmentProjectWithLakonaGameAsync(projectRoot, CliParser.ParseNewOptions([]));

            Assert.True(File.Exists(Path.Combine(interfacesDirectory, "IPingService.cs")));
            Assert.True(File.Exists(Path.Combine(interfacesDirectory, "SharedDtos.cs")));
            Assert.True(File.Exists(Path.Combine(interfacesDirectory, "RpcContractIds.cs")));
            Assert.True(File.Exists(Path.Combine(interfacesDirectory, "IPingService.cs.meta")));
            Assert.True(File.Exists(Path.Combine(serverDirectory, "Services", "PingService.cs")));
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AugmentExistingStarterServerProjectWritesLakonaGameContractsUnderSharedContracts()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "lakona-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var serverDirectory = Path.Combine(projectRoot, "Server", "App");
            Directory.CreateDirectory(serverDirectory);
            Directory.CreateDirectory(Path.Combine(projectRoot, "Shared"));
            await File.WriteAllTextAsync(
                Path.Combine(serverDirectory, "Server.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """,
                TestContext.Current.CancellationToken);

            await new ProjectScaffolder().AugmentProjectWithLakonaGameAsync(projectRoot, CliParser.ParseNewOptions([]));

            Assert.True(File.Exists(Path.Combine(projectRoot, "Shared", "Contracts", "RpcContractIds.cs")));
            Assert.True(File.Exists(Path.Combine(projectRoot, "Shared", "Contracts", "Chat", "ChatProtocols.cs")));
            Assert.True(File.Exists(Path.Combine(projectRoot, "Shared", "Contracts", "Chat", "ChatMessages.cs")));
            Assert.False(File.Exists(Path.Combine(projectRoot, "Shared", "Chat", "ChatProtocols.cs")));
            Assert.False(File.Exists(Path.Combine(projectRoot, "Shared", "Chat", "ChatMessages.cs")));
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GeneratedProjectChatFlowUsesActorAndHotfix()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "lakona-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(projectRoot, "Server", "App"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Shared"));
            await File.WriteAllTextAsync(
                Path.Combine(projectRoot, "Server", "App", "Server.App.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """,
                TestContext.Current.CancellationToken);

            await new ProjectScaffolder().AugmentProjectWithLakonaGameAsync(projectRoot, CliParser.ParseNewOptions([]));

            var loginService = await File.ReadAllTextAsync(Path.Combine(projectRoot, "Server", "Hotfix", "Login", "LoginService.cs"), TestContext.Current.CancellationToken);
            var service = await File.ReadAllTextAsync(Path.Combine(projectRoot, "Server", "Hotfix", "Chat", "ChatService.cs"), TestContext.Current.CancellationToken);
            var binding = await File.ReadAllTextAsync(Path.Combine(projectRoot, "Server", "App", "Hosting", "ServiceBindingConfigurator.cs"), TestContext.Current.CancellationToken);
            var actor = await File.ReadAllTextAsync(Path.Combine(projectRoot, "Server", "App", "Chat", "ChatRoomActor.cs"), TestContext.Current.CancellationToken);

            Assert.Contains("class LoginService : ILoginService", loginService, StringComparison.Ordinal);
            Assert.Contains("AskAsync<ChatRoomActor", loginService, StringComparison.Ordinal);
            Assert.Contains("ILoginCallback", loginService, StringComparison.Ordinal);
            Assert.Contains("IActorRuntime", service, StringComparison.Ordinal);
            Assert.Contains("AskAsync<ChatRoomActor", service, StringComparison.Ordinal);
            Assert.Contains("FilterMessage", service, StringComparison.Ordinal);
            Assert.Contains("class ChatRoomActor : Actor", actor, StringComparison.Ordinal);
            Assert.Contains("""LoadHotfixType("Server.Hotfix.Chat.ChatService")""", binding, StringComparison.Ordinal);
            Assert.Contains("""LoadHotfixType("Server.Hotfix.Login.LoginService")""", binding, StringComparison.Ordinal);
            Assert.Contains("LoginServiceBinder.Bind", binding, StringComparison.Ordinal);
            Assert.Contains("ChatServiceBinder.Bind", binding, StringComparison.Ordinal);
            Assert.DoesNotContain("AllServicesBinder.BindAll", binding, StringComparison.Ordinal);
            Assert.DoesNotContain("static readonly ChatRoom", service + actor, StringComparison.Ordinal);
            Assert.DoesNotContain("ConcurrentDictionary", actor, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void ToolFileWriter_WriteText_NormalizesNewlinesAndCreatesDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "lakona-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "nested", "file.txt");

            ToolFileWriter.WriteText(path, "﻿alpha\r\nbeta");

            var bytes = File.ReadAllBytes(path);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "File should not start with UTF-8 BOM");
            Assert.Equal("alpha\nbeta\n", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ToolFileWriter_WriteTextIfMissing_DoesNotOverwriteExistingFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "lakona-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "file.txt");
            Directory.CreateDirectory(root);
            File.WriteAllText(path, "existing");

            ToolFileWriter.WriteTextIfMissing(path, "replacement");

            Assert.Equal("existing", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AugmentExistingStarterServerProjectWritesUtf8NoBomWithLf()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "lakona-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var serverDirectory = Path.Combine(projectRoot, "Server", "App");
            Directory.CreateDirectory(serverDirectory);
            Directory.CreateDirectory(Path.Combine(projectRoot, "Shared"));
            await File.WriteAllTextAsync(
                Path.Combine(serverDirectory, "Server.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """,
                TestContext.Current.CancellationToken);

            await new ProjectScaffolder().AugmentProjectWithLakonaGameAsync(projectRoot, CliParser.ParseNewOptions([]));

            var programPath = Path.Combine(serverDirectory, "Program.cs");
            var bytes = await File.ReadAllBytesAsync(programPath, TestContext.Current.CancellationToken);
            var text = await File.ReadAllTextAsync(programPath, TestContext.Current.CancellationToken);

            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "File should not start with UTF-8 BOM");
            Assert.DoesNotContain("\r\n", text, StringComparison.Ordinal);
            Assert.EndsWith("\n", text, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void ProjectXmlMutator_UpsertsPropertiesAndPackageReferences()
    {
        var document = System.Xml.Linq.XDocument.Parse(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
              </PropertyGroup>
            </Project>
            """);
        var project = document.Root ?? throw new InvalidOperationException("Missing project root.");

        ProjectXmlMutator.SetProperty(project, "TargetFramework", "net10.0");
        ProjectXmlMutator.RemoveProperty(project, "TargetFrameworks");
        ProjectXmlMutator.EnsurePackageReference(project, "Lakona.Game.Server", "1.2.3");
        ProjectXmlMutator.EnsurePackageReference(
            project,
            "Lakona.Game.Server.Generators",
            "2.3.4",
            ("PrivateAssets", "all"),
            ("OutputItemType", "Analyzer"));

        var xml = document.ToString();

        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("TargetFrameworks", xml, StringComparison.Ordinal);
        Assert.Contains(@"PackageReference Include=""Lakona.Game.Server"" Version=""1.2.3""", xml, StringComparison.Ordinal);
        Assert.Contains(@"PackageReference Include=""Lakona.Game.Server.Generators"" Version=""2.3.4"" PrivateAssets=""all"" OutputItemType=""Analyzer""", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void GameDependencyPlanner_DefaultClusterServerIncludesRpcPackages()
    {
        var plan = GameDependencyPlanner.CreateServerPlan(CliParser.ParseNewOptions([]));

        Assert.Contains(plan.PackageReferences, reference => reference.Id == "Microsoft.Extensions.Hosting");
        Assert.Contains(plan.PackageReferences, reference => reference.Id == "Lakona.Game.Server");
        Assert.Contains(plan.PackageReferences, reference => reference.Id == "Lakona.Game.Server.Generators" && reference.OutputItemType == "Analyzer");
        Assert.Contains(plan.PackageReferences, reference => reference.Id == "Lakona.Game.Server.Hotfix");
        Assert.Contains(plan.PackageReferences, reference => reference.Id == "Lakona.Game.Server.Hotfix.Generators" && reference.OutputItemType == "Analyzer");
        Assert.Contains(plan.PackageReferences, reference => reference.Id == "Lakona.Rpc.Server");
        Assert.Contains(plan.PackageReferences, reference => reference.Id == "Lakona.Rpc.Transport.Kcp");
        Assert.Contains(plan.PackageReferences, reference => reference.Id == "Lakona.Rpc.Analyzers");
        Assert.Contains(plan.PackageReferences, reference => reference.Id == "Lakona.Game.Cluster");
        Assert.Contains(plan.PackageReferences, reference => reference.Id == "Lakona.Game.Cluster.Rpc");
        Assert.Equal(10, plan.PackageReferences.Count);
    }

    [Fact]
    public void ToolCodegenValues_ReturnsConsistentTransportAndSerializerValues()
    {
        var websocket = ToolCodegenValues.Create(TransportKind.WebSocket, SerializerKind.Json);
        var kcp = ToolCodegenValues.Create(TransportKind.Kcp, SerializerKind.MemoryPack);

        Assert.Equal("using Lakona.Rpc.Transport.WebSocket;", websocket.TransportUsing);
        Assert.Equal("using Lakona.Rpc.Serializer.Json;", websocket.SerializerUsing);
        Assert.Equal("new WsTransport($\"ws://{_serverHost}:{_serverPort}{NormalizePath(_serverPath)}\")", websocket.UnityChatTransportConstruction);
        Assert.Equal("new JsonRpcSerializer()", websocket.SerializerConstruction);
        Assert.Equal("/ws", websocket.DefaultPath);
        Assert.Equal("using Lakona.Rpc.Transport.Kcp;", kcp.TransportUsing);
        Assert.Equal("using Lakona.Rpc.Serializer.MemoryPack;", kcp.SerializerUsing);
        Assert.Equal("new KcpTransport(_serverHost, _serverPort)", kcp.UnityChatTransportConstruction);
        Assert.Equal("new MemoryPackRpcSerializer()", kcp.SerializerConstruction);
        Assert.Equal("", kcp.DefaultPath);
    }

    [Fact]
    public void GameDependencyPlanner_TransportVersions_MatchToolPackageVersions()
    {
        var tcpPlan = GameDependencyPlanner.CreateServerPlan(CliParser.ParseNewOptions(["--transport", "tcp"]));
        var wsPlan = GameDependencyPlanner.CreateServerPlan(CliParser.ParseNewOptions(["--transport", "websocket"]));
        var kcpPlan = GameDependencyPlanner.CreateServerPlan(CliParser.ParseNewOptions([]));

        var tcpVersion = tcpPlan.PackageReferences.Single(r => r.Id == "Lakona.Rpc.Transport.Tcp").Version;
        var wsVersion = wsPlan.PackageReferences.Single(r => r.Id == "Lakona.Rpc.Transport.WebSocket").Version;
        var kcpVersion = kcpPlan.PackageReferences.Single(r => r.Id == "Lakona.Rpc.Transport.Kcp").Version;

        Assert.Equal(ToolPackageVersions.LakonaRpcTransportTcp, tcpVersion);
        Assert.Equal(ToolPackageVersions.LakonaRpcTransportWebSocket, wsVersion);
        Assert.Equal(ToolPackageVersions.LakonaRpcTransportKcp, kcpVersion);
    }

    [Fact]
    public void ServerProjectTemplate_RenderServerProject_UsesToolPackageVersionsForTransport()
    {
        var tcpSource = ToolTemplates.RenderServerProject(new NewCommandOptions(
            Name: "Test", OutputPath: ".", ClientEngine: "unity", Transport: "tcp",
            NetworkProfile: ProjectConventions.DefaultNetworkProfile, Serializer: ProjectConventions.DefaultSerializer,
            Persistence: ProjectConventions.DefaultPersistence, NuGetForUnitySource: ProjectConventions.DefaultNuGetForUnitySource,
            DeployProfile: ProjectConventions.DefaultDeployProfile));
        var wsSource = ToolTemplates.RenderServerProject(new NewCommandOptions(
            Name: "Test", OutputPath: ".", ClientEngine: "unity", Transport: "websocket",
            NetworkProfile: ProjectConventions.DefaultNetworkProfile, Serializer: ProjectConventions.DefaultSerializer,
            Persistence: ProjectConventions.DefaultPersistence, NuGetForUnitySource: ProjectConventions.DefaultNuGetForUnitySource,
            DeployProfile: ProjectConventions.DefaultDeployProfile));
        var kcpSource = ToolTemplates.RenderServerProject(new NewCommandOptions(
            Name: "Test", OutputPath: ".", ClientEngine: "unity", Transport: "kcp",
            NetworkProfile: ProjectConventions.DefaultNetworkProfile, Serializer: ProjectConventions.DefaultSerializer,
            Persistence: ProjectConventions.DefaultPersistence, NuGetForUnitySource: ProjectConventions.DefaultNuGetForUnitySource,
            DeployProfile: ProjectConventions.DefaultDeployProfile));

        Assert.Contains($$"""<PackageReference Include="Lakona.Rpc.Transport.Tcp" Version="{{ToolPackageVersions.LakonaRpcTransportTcp}}" />""", tcpSource, StringComparison.Ordinal);
        Assert.Contains($$"""<PackageReference Include="Lakona.Rpc.Transport.WebSocket" Version="{{ToolPackageVersions.LakonaRpcTransportWebSocket}}" />""", wsSource, StringComparison.Ordinal);
        Assert.Contains($$"""<PackageReference Include="Lakona.Rpc.Transport.Kcp" Version="{{ToolPackageVersions.LakonaRpcTransportKcp}}" />""", kcpSource, StringComparison.Ordinal);
    }

    [Fact]
    public void GameDependencyPlanner_RpcPackageVersions_MatchToolPackageVersions()
    {
        var plan = GameDependencyPlanner.CreateServerPlan(CliParser.ParseNewOptions([]));

        var serverVersion = plan.PackageReferences.Single(r => r.Id == "Lakona.Rpc.Server").Version;
        var analyzersVersion = plan.PackageReferences.Single(r => r.Id == "Lakona.Rpc.Analyzers").Version;

        Assert.Equal(ToolPackageVersions.LakonaRpcServer, serverVersion);
        Assert.Equal(ToolPackageVersions.LakonaRpcAnalyzers, analyzersVersion);
    }

    [Fact]
    public void GameDependencyPlanner_SerializerJsonVersion_MatchesToolPackageVersions()
    {
        var plan = GameDependencyPlanner.CreateServerPlan(CliParser.ParseNewOptions(["--serializer", "json"]));

        var serializerVersion = plan.PackageReferences.Single(r => r.Id == "Lakona.Rpc.Serializer.Json").Version;

        Assert.Equal(ToolPackageVersions.LakonaRpcSerializerJson, serializerVersion);
    }

    [Fact]
    public void ServerProjectTemplate_RenderServerProject_UsesToolPackageVersionsForRpcPackages()
    {
        var source = ToolTemplates.RenderServerProject(new NewCommandOptions(
            Name: "Test", OutputPath: ".", ClientEngine: "unity", Transport: "kcp",
            NetworkProfile: ProjectConventions.DefaultNetworkProfile, Serializer: "json",
            Persistence: ProjectConventions.DefaultPersistence, NuGetForUnitySource: ProjectConventions.DefaultNuGetForUnitySource,
            DeployProfile: ProjectConventions.DefaultDeployProfile));

        Assert.Contains($$"""<PackageReference Include="Lakona.Rpc.Server" Version="{{ToolPackageVersions.LakonaRpcServer}}" />""", source, StringComparison.Ordinal);
        Assert.Contains($$"""<PackageReference Include="Lakona.Rpc.Analyzers" Version="{{ToolPackageVersions.LakonaRpcAnalyzers}}" PrivateAssets="all">""", source, StringComparison.Ordinal);
        Assert.Contains($$"""<PackageReference Include="Lakona.Rpc.Serializer.Json" Version="{{ToolPackageVersions.LakonaRpcSerializerJson}}" />""", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectXmlMutator_EnsuresNuGetForUnityPackage()
    {
        var document = System.Xml.Linq.XDocument.Parse("<packages><package id=\"Lakona.Game.Client\" version=\"0.0.1\" /></packages>");
        var packages = document.Root ?? throw new InvalidOperationException("Missing packages root.");

        ProjectXmlMutator.EnsureNuGetForUnityPackage(packages, "Lakona.Game.Client", "1.2.3");
        ProjectXmlMutator.EnsureNuGetForUnityPackage(packages, "Lakona.Game.Abstractions", "2.3.4");

        var xml = document.ToString();

        Assert.Contains(@"<package id=""Lakona.Game.Client"" version=""1.2.3"" manuallyInstalled=""true"" />", xml, StringComparison.Ordinal);
        Assert.Contains(@"<package id=""Lakona.Game.Abstractions"" version=""2.3.4"" manuallyInstalled=""true"" />", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AugmentProjectWithLakonaGame_RegistersUnityScenesInEditorBuildSettings()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "lakona-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(projectRoot, "Server", "App"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Shared"));
            await File.WriteAllTextAsync(
                Path.Combine(projectRoot, "Server", "App", "Server.App.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """,
                TestContext.Current.CancellationToken);

            await new ProjectScaffolder().AugmentProjectWithLakonaGameAsync(projectRoot, CliParser.ParseNewOptions([]));

            var settingsPath = Path.Combine(projectRoot, "Client", "ProjectSettings", "EditorBuildSettings.asset");
            Assert.True(File.Exists(settingsPath), "EditorBuildSettings.asset should be created.");
            var settings = await File.ReadAllTextAsync(settingsPath, TestContext.Current.CancellationToken);

            Assert.Contains("Assets/Scenes/LoginScene.unity", settings, StringComparison.Ordinal);
            Assert.Contains("Assets/Scenes/ChatScene.unity", settings, StringComparison.Ordinal);
            Assert.Contains("enabled: 1", settings, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AugmentProjectWithLakonaGame_GeneratedChatSceneIncludesUnityStandardHeaderSections()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "lakona-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(projectRoot, "Server", "App"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Shared"));
            await File.WriteAllTextAsync(
                Path.Combine(projectRoot, "Server", "App", "Server.App.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """,
                TestContext.Current.CancellationToken);

            await new ProjectScaffolder().AugmentProjectWithLakonaGameAsync(projectRoot, CliParser.ParseNewOptions([]));

            var chatScenePath = Path.Combine(projectRoot, "Client", "Assets", "Scenes", "ChatScene.unity");
            Assert.True(File.Exists(chatScenePath), "ChatScene.unity should be created.");
            var scene = await File.ReadAllTextAsync(chatScenePath, TestContext.Current.CancellationToken);

            Assert.Contains("OcclusionCullingSettings", scene, StringComparison.Ordinal);
            Assert.Contains("RenderSettings", scene, StringComparison.Ordinal);
            Assert.Contains("LightmapSettings", scene, StringComparison.Ordinal);
            Assert.Contains("NavMeshSettings", scene, StringComparison.Ordinal);
            Assert.Contains("Lakona.Game Chat UI", scene, StringComparison.Ordinal);
            Assert.Contains("Main Camera", scene, StringComparison.Ordinal);
            Assert.Contains("SceneRoots:", scene, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void RenderGodotTheme_ContainsExpectedStyleBoxes()
    {
        var tres = ChatClientTemplates.RenderGodotTheme();

        Assert.Contains("[sub_resource type=\"StyleBoxFlat\" id=\"1\"]", tres, StringComparison.Ordinal);
        Assert.Contains("[sub_resource type=\"StyleBoxFlat\" id=\"2\"]", tres, StringComparison.Ordinal);
        Assert.Contains("[sub_resource type=\"StyleBoxFlat\" id=\"3\"]", tres, StringComparison.Ordinal);
        Assert.Contains("[sub_resource type=\"StyleBoxFlat\" id=\"4\"]", tres, StringComparison.Ordinal);
        Assert.Contains("[sub_resource type=\"StyleBoxFlat\" id=\"5\"]", tres, StringComparison.Ordinal);
        Assert.Contains("[sub_resource type=\"StyleBoxFlat\" id=\"6\"]", tres, StringComparison.Ordinal);
        Assert.Contains("[sub_resource type=\"StyleBoxFlat\" id=\"7\"]", tres, StringComparison.Ordinal);
        Assert.Contains("load_steps=8", tres, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderGodotTheme_ContainsDefaultTypeStyles()
    {
        var tres = ChatClientTemplates.RenderGodotTheme();

        Assert.Contains("Button/colors/font_color", tres, StringComparison.Ordinal);
        Assert.Contains("Button/styles/normal", tres, StringComparison.Ordinal);
        Assert.Contains("LineEdit/colors/font_color", tres, StringComparison.Ordinal);
        Assert.Contains("default_font_size", tres, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderGodotTheme_ContainsTypeVariations()
    {
        var tres = ChatClientTemplates.RenderGodotTheme();

        Assert.Contains("TitleLabel/font_sizes/font_size", tres, StringComparison.Ordinal);
        Assert.Contains("LoginPanel/styles/panel", tres, StringComparison.Ordinal);
        Assert.Contains("PanelVBox/constants/separation", tres, StringComparison.Ordinal);
        Assert.Contains("ChatHeader/styles/panel", tres, StringComparison.Ordinal);
        Assert.Contains("ChatFooter/styles/panel", tres, StringComparison.Ordinal);
        Assert.Contains("SendRow/constants/separation", tres, StringComparison.Ordinal);
        Assert.Contains("PageMargin/constants/margin_left", tres, StringComparison.Ordinal);
        Assert.Contains("StatusLabel/colors/font_color", tres, StringComparison.Ordinal);
        Assert.Contains("OnlineCount/colors/font_color", tres, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderGodotTheme_ContainsAllColorConstants()
    {
        var tres = ChatClientTemplates.RenderGodotTheme();

        Assert.Contains("0, 1, 0.4", tres, StringComparison.Ordinal);        // Accent
        Assert.Contains("0, 0.667, 0.267", tres, StringComparison.Ordinal);  // AccentDim
        Assert.Contains("0.533, 0.8, 0.6", tres, StringComparison.Ordinal);  // TextBody
        Assert.Contains("0.267, 0.533, 0.333", tres, StringComparison.Ordinal); // TextDim
        Assert.Contains("1, 0.267, 0.267", tres, StringComparison.Ordinal);  // Error
        Assert.Contains("1, 1, 0", tres, StringComparison.Ordinal);           // Warning
        Assert.Contains("0.059, 0.102, 0.059", tres, StringComparison.Ordinal); // BgPanel
        Assert.Contains("0.02, 0.039, 0.039", tres, StringComparison.Ordinal);  // BgInput
        Assert.Contains("0.039, 0.059, 0.039", tres, StringComparison.Ordinal); // BgBase
    }

    [Fact]
    public void RenderGodotLoginTscn_ContainsFullNodeTree()
    {
        var tscn = ChatClientTemplates.RenderGodotLoginTscn();

        // load_steps=3 (script + theme + scene)
        Assert.Contains("[gd_scene load_steps=3 format=3]", tscn, StringComparison.Ordinal);

        // Two ext_resources
        Assert.Contains("[ext_resource type=\"Script\" path=\"res://Scripts/Login/LoginScene.cs\" id=\"1\"]", tscn, StringComparison.Ordinal);
        Assert.Contains("[ext_resource type=\"Theme\" path=\"res://Themes/LakonaTheme.tres\" id=\"2\"]", tscn, StringComparison.Ordinal);

        // Root: LoginScene (Control)
        Assert.Contains("[node name=\"LoginScene\" type=\"Control\"]", tscn, StringComparison.Ordinal);
        Assert.Contains("theme = ExtResource(\"2\")", tscn, StringComparison.Ordinal);
        Assert.Contains("script = ExtResource(\"1\")", tscn, StringComparison.Ordinal);

        // Background (ColorRect, parent=".")
        Assert.Contains("[node name=\"Background\" type=\"ColorRect\" parent=\".\"]", tscn, StringComparison.Ordinal);

        // Scanlines (ColorRect, parent=".")
        Assert.Contains("[node name=\"Scanlines\" type=\"ColorRect\" parent=\".\"]", tscn, StringComparison.Ordinal);
        Assert.Contains("mouse_filter = 2", tscn, StringComparison.Ordinal);

        // Center (CenterContainer, parent=".")
        Assert.Contains("[node name=\"Center\" type=\"CenterContainer\" parent=\".\"]", tscn, StringComparison.Ordinal);

        // LoginPanel (PanelContainer, parent="Center")
        Assert.Contains("[node name=\"LoginPanel\" type=\"PanelContainer\" parent=\"Center\"]", tscn, StringComparison.Ordinal);

        // PanelContent (VBoxContainer, parent="Center/LoginPanel")
        Assert.Contains("[node name=\"PanelContent\" type=\"VBoxContainer\" parent=\"Center/LoginPanel\"]", tscn, StringComparison.Ordinal);

        // Title (Label)
        Assert.Contains("[node name=\"Title\" type=\"Label\" parent=\"Center/LoginPanel/PanelContent\"]", tscn, StringComparison.Ordinal);
        Assert.Contains("text = \"LAKONA\"", tscn, StringComparison.Ordinal);

        // NameLabel (Label)
        Assert.Contains("[node name=\"NameLabel\" type=\"Label\" parent=\"Center/LoginPanel/PanelContent\"]", tscn, StringComparison.Ordinal);
        Assert.Contains("text = \"NAME:\"", tscn, StringComparison.Ordinal);

        // NameField (LineEdit)
        Assert.Contains("[node name=\"NameField\" type=\"LineEdit\" parent=\"Center/LoginPanel/PanelContent\"]", tscn, StringComparison.Ordinal);
        Assert.Contains("max_length = 20", tscn, StringComparison.Ordinal);

        // ConnectButton (Button)
        Assert.Contains("[node name=\"ConnectButton\" type=\"Button\" parent=\"Center/LoginPanel/PanelContent\"]", tscn, StringComparison.Ordinal);
        Assert.Contains("text = \"CONNECT\"", tscn, StringComparison.Ordinal);

        // StatusLabel (Label)
        Assert.Contains("[node name=\"StatusLabel\" type=\"Label\" parent=\"Center/LoginPanel/PanelContent\"]", tscn, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderGodotLoginTscn_InteractiveNodesHaveUniqueNames()
    {
        var tscn = ChatClientTemplates.RenderGodotLoginTscn();

        // Extract the sections that follow each interactive node header
        // NameField should have unique_name_in_owner = true
        var nameFieldIndex = tscn.IndexOf("[node name=\"NameField\"", StringComparison.Ordinal);
        Assert.True(nameFieldIndex >= 0, "NameField node missing");
        var afterNameField = tscn[(nameFieldIndex + "[node name=\"NameField\" type=\"LineEdit\" parent=\"Center/LoginPanel/PanelContent\"]".Length)..];
        var nextNodeAfterNameField = afterNameField.IndexOf("[node name=", StringComparison.Ordinal);
        var nameFieldSection = nextNodeAfterNameField >= 0 ? afterNameField[..nextNodeAfterNameField] : afterNameField;
        Assert.Contains("unique_name_in_owner = true", nameFieldSection, StringComparison.Ordinal);

        // ConnectButton should have unique_name_in_owner = true
        var connectIndex = tscn.IndexOf("[node name=\"ConnectButton\"", StringComparison.Ordinal);
        Assert.True(connectIndex >= 0, "ConnectButton node missing");
        var afterConnect = tscn[(connectIndex + "[node name=\"ConnectButton\" type=\"Button\" parent=\"Center/LoginPanel/PanelContent\"]".Length)..];
        var nextNodeAfterConnect = afterConnect.IndexOf("[node name=", StringComparison.Ordinal);
        var connectSection = nextNodeAfterConnect >= 0 ? afterConnect[..nextNodeAfterConnect] : afterConnect;
        Assert.Contains("unique_name_in_owner = true", connectSection, StringComparison.Ordinal);

        // StatusLabel should have unique_name_in_owner = true
        var statusIndex = tscn.IndexOf("[node name=\"StatusLabel\"", StringComparison.Ordinal);
        Assert.True(statusIndex >= 0, "StatusLabel node missing");
        var afterStatus = tscn[(statusIndex + "[node name=\"StatusLabel\" type=\"Label\" parent=\"Center/LoginPanel/PanelContent\"]".Length)..];
        var nextNodeAfterStatus = afterStatus.IndexOf("[node name=", StringComparison.Ordinal);
        var statusSection = nextNodeAfterStatus >= 0 ? afterStatus[..nextNodeAfterStatus] : afterStatus;
        Assert.Contains("unique_name_in_owner = true", statusSection, StringComparison.Ordinal);

        // Nodes that should NOT have unique_name_in_owner — check the LoginScene root section
        var loginSceneEnd = tscn.IndexOf("[node name=\"Background\"", StringComparison.Ordinal);
        Assert.True(loginSceneEnd >= 0, "Background node missing — cannot find LoginScene section boundary");
        var loginSceneSection = tscn[..loginSceneEnd];
        Assert.DoesNotContain("unique_name_in_owner", loginSceneSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderGodotLoginTscn_NodesUseThemeTypeVariations()
    {
        var tscn = ChatClientTemplates.RenderGodotLoginTscn();

        Assert.Contains("theme_type_variation = LoginPanel", tscn, StringComparison.Ordinal);
        Assert.Contains("theme_type_variation = PanelVBox", tscn, StringComparison.Ordinal);
        Assert.Contains("theme_type_variation = TitleLabel", tscn, StringComparison.Ordinal);
        Assert.Contains("theme_type_variation = NameLabel", tscn, StringComparison.Ordinal);
        Assert.Contains("theme_type_variation = StatusLabel", tscn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AugmentProjectWithLakonaGame_GeneratedLoginSceneIncludesUnityStandardHeaderSections()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "lakona-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(projectRoot, "Server", "App"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Shared"));
            await File.WriteAllTextAsync(
                Path.Combine(projectRoot, "Server", "App", "Server.App.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """,
                TestContext.Current.CancellationToken);

            await new ProjectScaffolder().AugmentProjectWithLakonaGameAsync(projectRoot, CliParser.ParseNewOptions([]));

            var loginScenePath = Path.Combine(projectRoot, "Client", "Assets", "Scenes", "LoginScene.unity");
            Assert.True(File.Exists(loginScenePath), "LoginScene.unity should be created.");
            var scene = await File.ReadAllTextAsync(loginScenePath, TestContext.Current.CancellationToken);

            Assert.Contains("OcclusionCullingSettings", scene, StringComparison.Ordinal);
            Assert.Contains("RenderSettings", scene, StringComparison.Ordinal);
            Assert.Contains("LightmapSettings", scene, StringComparison.Ordinal);
            Assert.Contains("NavMeshSettings", scene, StringComparison.Ordinal);
            Assert.Contains("Lakona.Game Login UI", scene, StringComparison.Ordinal);
            Assert.Contains("Main Camera", scene, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }
}
