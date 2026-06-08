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

        Assert.Contains("using Server.Hosting;", source, StringComparison.Ordinal);
        Assert.Contains("using Lakona.Game.Server.Hosting;", source, StringComparison.Ordinal);
        Assert.Contains("return await LakonaGameServer.RunAsync(args", source, StringComparison.Ordinal);
        Assert.Contains("ServiceBindingConfigurator.Bind", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using Server.Hosting.Advanced;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LakonaGameGeneratedApplication", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LakonaGameRuntimeOptions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ClusterOptions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddULinkRpcServer", source, StringComparison.Ordinal);
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

        Assert.Contains("using Server.Hosting;", source, StringComparison.Ordinal);
        Assert.Contains("using Lakona.Game.Server.Hosting;", source, StringComparison.Ordinal);
        Assert.Contains("return await LakonaGameServer.RunAsync(args", source, StringComparison.Ordinal);
        Assert.Contains("ServiceBindingConfigurator.Bind", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using Server.Hosting.Advanced;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LakonaGameGeneratedApplication", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LakonaGameRuntimeOptions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ClusterOptions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddULinkRpcServer", source, StringComparison.Ordinal);
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
        var sharedProject = ToolTemplates.RenderSharedProjectHotfixItemGroup();
        var sharedAssemblyInfo = ToolTemplates.RenderSharedHotfixAssemblyInfo();
        var sharedContractIds = ToolTemplates.RenderSharedRpcContractIds();
        var sharedProtocols = ToolTemplates.RenderSharedChatProtocols();
        var sharedMessages = ToolTemplates.RenderSharedChatMessages();
        var hotfixProject = ToolTemplates.RenderHotfixProject();
        var hotfixChatSystem = ToolTemplates.RenderHotfixChatSystem();
        var appSettings = ToolTemplates.RenderServerAppSettings(options);
        var program = ToolTemplates.RenderServerProgram(options);
        var generatedApplication = ToolTemplates.RenderGeneratedServerApplication(options);
        var chatRoomActor = ToolTemplates.RenderServerChatRoomActor();
        var chatRules = ToolTemplates.RenderServerChatRules();
        var chatServiceImpl = ToolTemplates.RenderServerChatServiceImpl();
        var generatedText = string.Concat(
            solution,
            project,
            sharedProject,
            sharedAssemblyInfo,
            sharedContractIds,
            sharedProtocols,
            sharedMessages,
            hotfixProject,
            hotfixChatSystem,
            appSettings,
            program,
            generatedApplication,
            chatRoomActor,
            chatRules,
            chatServiceImpl);

        Assert.Contains(@"<Project Path=""Hotfix/Server.Hotfix.csproj"" />", solution, StringComparison.Ordinal);
        Assert.Contains(@"<ProjectReference Include=""..\Hotfix\Server.Hotfix.csproj"" ReferenceOutputAssembly=""false"" />", project, StringComparison.Ordinal);
        Assert.Contains(@"PackageReference Include=""Lakona.Game.Server.Hotfix""", project, StringComparison.Ordinal);
        Assert.Contains(@"PackageReference Include=""Lakona.Game.Server.Generators""", project, StringComparison.Ordinal);
        Assert.Contains(@"PrivateAssets=""all"" OutputItemType=""Analyzer""", project, StringComparison.Ordinal);
        Assert.Contains(@"PackageReference Include=""Lakona.Game.Server.Hotfix.Abstractions""", sharedProject, StringComparison.Ordinal);
        Assert.Contains(@"PackageReference Include=""Lakona.Game.Server.Hotfix.Generators""", sharedProject, StringComparison.Ordinal);
        Assert.Contains(@"InternalsVisibleTo(""Server.Hotfix"")", sharedAssemblyInfo, StringComparison.Ordinal);
        Assert.Contains("public const int Chat = 2;", sharedContractIds, StringComparison.Ordinal);
        Assert.Contains("public const int MessageReceived = 1;", sharedContractIds, StringComparison.Ordinal);
        Assert.Contains("[RpcService(RpcContractIds.Services.Chat, NotificationContract = typeof(IChatCallback))]", sharedProtocols, StringComparison.Ordinal);
        Assert.Contains("interface IChatService", sharedProtocols, StringComparison.Ordinal);
        Assert.Contains("interface IChatCallback", sharedProtocols, StringComparison.Ordinal);
        Assert.Contains("ChatJoinRequest", sharedMessages, StringComparison.Ordinal);
        Assert.Contains("ChatMessage", sharedMessages, StringComparison.Ordinal);
        Assert.Contains(@"ProjectReference Include=""..\..\Shared\Shared.csproj""", hotfixProject, StringComparison.Ordinal);
        Assert.Contains(@"PackageReference Include=""Lakona.Game.Server.Hotfix.Abstractions""", hotfixProject, StringComparison.Ordinal);
        Assert.Contains("class ChatRulesSystem", hotfixChatSystem, StringComparison.Ordinal);
        Assert.Contains("[HotfixSystemOf(typeof(ChatRuleState))]", hotfixChatSystem, StringComparison.Ordinal);
        Assert.Contains("FilterMessage", hotfixChatSystem, StringComparison.Ordinal);
        Assert.Contains("class ChatRoomActor : Actor", chatRoomActor, StringComparison.Ordinal);
        Assert.Contains("IActorRuntime", chatServiceImpl, StringComparison.Ordinal);
        Assert.Contains("class ChatServiceImpl", chatServiceImpl, StringComparison.Ordinal);
        Assert.Contains("IChatService", chatServiceImpl, StringComparison.Ordinal);
        Assert.Contains("HotfixDispatch.Invoke", chatRules, StringComparison.Ordinal);
        Assert.DoesNotContain("static readonly ChatRoom", generatedText, StringComparison.Ordinal);
        Assert.DoesNotContain("SanitizeMessage", hotfixChatSystem, StringComparison.Ordinal);
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
            var serverDirectory = Path.Combine(projectRoot, "Server", "Server");
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
            Assert.Contains("PingServiceBinder.BindFactory", serviceBindingConfigurator, StringComparison.Ordinal);
            Assert.Contains("ActivatorUtilities.CreateInstance<PingService>(services)", serviceBindingConfigurator, StringComparison.Ordinal);
            Assert.Contains("ChatServiceBinder.Bind", serviceBindingConfigurator, StringComparison.Ordinal);
            Assert.Contains("ActivatorUtilities.CreateInstance<ChatServiceImpl>(services, callback)", serviceBindingConfigurator, StringComparison.Ordinal);
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
            var serverDirectory = Path.Combine(projectRoot, "Server", "Server");
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

            var project = await File.ReadAllTextAsync(
                Path.Combine(serverDirectory, "Server.csproj"),
                TestContext.Current.CancellationToken);

            Assert.Contains(@"<Target Name=""CopyHotfixOutput"" AfterTargets=""Build"">", project, StringComparison.Ordinal);
            Assert.Contains(@"DestinationFolder=""$(OutDir)hotfix\""", project, StringComparison.Ordinal);
            Assert.Contains("Server.Hotfix.dll", project, StringComparison.Ordinal);
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
        Assert.Contains("[RpcMethod(RpcContractIds.ChatServiceMethods.JoinAsync)]", source, StringComparison.Ordinal);
        Assert.Contains("[RpcMethod(RpcContractIds.ChatServiceMethods.SendAsync)]", source, StringComparison.Ordinal);
        Assert.Contains("[RpcMethod(RpcContractIds.ChatServiceMethods.LeaveAsync)]", source, StringComparison.Ordinal);
        Assert.Contains("[RpcNotification(RpcContractIds.ChatNotifications.MessageReceived)]", source, StringComparison.Ordinal);
        Assert.Contains("[RpcNotification(RpcContractIds.ChatNotifications.UserJoined)]", source, StringComparison.Ordinal);
        Assert.Contains("[RpcNotification(RpcContractIds.ChatNotifications.UserLeft)]", source, StringComparison.Ordinal);
        Assert.Contains("OnMessageReceived", source, StringComparison.Ordinal);
        Assert.Contains("OnUserJoined", source, StringComparison.Ordinal);
        Assert.Contains("OnUserLeft", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[RpcService(2", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[RpcMethod(1)]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[RpcNotification(1)]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderSharedRpcContractIds_DefinesChatIds()
    {
        var source = ToolTemplates.RenderSharedRpcContractIds();

        Assert.Contains("namespace Shared.Contracts", source, StringComparison.Ordinal);
        Assert.Contains("public const int Chat = 2;", source, StringComparison.Ordinal);
        Assert.Contains("public const int JoinAsync = 1;", source, StringComparison.Ordinal);
        Assert.Contains("public const int SendAsync = 2;", source, StringComparison.Ordinal);
        Assert.Contains("public const int LeaveAsync = 3;", source, StringComparison.Ordinal);
        Assert.Contains("public const int MessageReceived = 1;", source, StringComparison.Ordinal);
        Assert.Contains("public const int UserJoined = 2;", source, StringComparison.Ordinal);
        Assert.Contains("public const int UserLeft = 3;", source, StringComparison.Ordinal);
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
            ("Server/Server/Program.cs", ToolTemplates.RenderServerProgram(options)),
            ("Server/Server/Hosting/Advanced/LakonaGameGeneratedApplication.cs", ToolTemplates.RenderGeneratedServerApplication(options)),
            ("Server/Server/Hosting/ServiceBindingConfigurator.cs", ToolTemplates.RenderServiceBindingConfigurator()),
            ("Server/Server/RealtimeProgram.cs", ToolTemplates.RenderServerProgram(realtimeOptions)),
            ("Server/Server/Hosting/Advanced/RealtimeLakonaGameGeneratedApplication.cs", ToolTemplates.RenderGeneratedServerApplication(realtimeOptions)),
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
            ("Client/Assets/Scripts/Chat/ChatClient.cs", ToolTemplates.RenderClientChatClient()),
            ("Client/Assets/Scripts/Chat/ChatUI.cs", ToolTemplates.RenderClientChatUI(CliParser.ParseNewOptions([])))
        };

        AssertGeneratedSourcesParseAsCSharp9(sources);
    }

    [Fact]
    public void RenderServerChatTemplates_ParseAsCurrentCSharp()
    {
        var sources = new[]
        {
            ("Server/Server/Chat/ChatRoomActor.cs", ToolTemplates.RenderServerChatRoomActor()),
            ("Server/Server/Chat/ChatRules.cs", ToolTemplates.RenderServerChatRules()),
            ("Server/Server/Chat/ChatServiceImpl.cs", ToolTemplates.RenderServerChatServiceImpl()),
            ("Server/Hotfix/Chat/ChatRulesSystem.cs", ToolTemplates.RenderHotfixChatSystem())
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
        Assert.Contains("ChatRules", source, StringComparison.Ordinal);
        Assert.Contains("FilterMessage", source, StringComparison.Ordinal);
        Assert.Contains("Broadcast(cb => cb.OnMessageReceived", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConcurrentDictionary", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConcurrentQueue", source, StringComparison.Ordinal);
        Assert.DoesNotContain("lock (", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderServerChatServiceImpl_UsesActorRuntime()
    {
        var source = ToolTemplates.RenderServerChatServiceImpl();

        Assert.Contains("class ChatServiceImpl : IChatService", source, StringComparison.Ordinal);
        Assert.Contains("private readonly IActorRuntime _actors;", source, StringComparison.Ordinal);
        Assert.Contains("public ChatServiceImpl(IChatCallback callback, IActorRuntime actors)", source, StringComparison.Ordinal);
        Assert.Contains("ActorId.From(\"chat:global\")", source, StringComparison.Ordinal);
        Assert.Contains("_actors.AskAsync<ChatRoomActor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("static readonly ChatRoom", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new ChatRoom", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderServiceBindingConfigurator_UsesDependencyInjectionForNotificationServices()
    {
        var source = ToolTemplates.RenderServiceBindingConfigurator();

        Assert.Contains("using Microsoft.Extensions.DependencyInjection;", source, StringComparison.Ordinal);
        Assert.Contains("using Server.Chat;", source, StringComparison.Ordinal);
        Assert.Contains("using Server.Services;", source, StringComparison.Ordinal);
        Assert.Contains("using Server.Generated;", source, StringComparison.Ordinal);
        Assert.Contains("public static void Bind(RpcServiceRegistry registry, IServiceProvider services)", source, StringComparison.Ordinal);
        Assert.Contains("PingServiceBinder.BindFactory", source, StringComparison.Ordinal);
        Assert.Contains("ActivatorUtilities.CreateInstance<PingService>(services)", source, StringComparison.Ordinal);
        Assert.Contains("ChatServiceBinder.Bind", source, StringComparison.Ordinal);
        Assert.Contains("ActivatorUtilities.CreateInstance<ChatServiceImpl>(services, callback)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AllServicesBinder.BindAll", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderServerChatRules_UsesHotfixDispatch()
    {
        var source = ToolTemplates.RenderServerChatRules();

        Assert.Contains("class ChatRules", source, StringComparison.Ordinal);
        Assert.Contains("using Shared.Contracts.Chat;", source, StringComparison.Ordinal);
        Assert.Contains("HotfixDispatch.Invoke<ChatRuleState, string, string>", source, StringComparison.Ordinal);
        Assert.Contains("\"FilterMessage\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderSharedChatRuleState_DefinesHotfixState()
    {
        var source = ToolTemplates.RenderSharedChatRuleState();

        Assert.Contains("[HotfixState]", source, StringComparison.Ordinal);
        Assert.Contains("partial class ChatRuleState", source, StringComparison.Ordinal);
        Assert.Contains("namespace Shared.Contracts.Chat", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderClientChatClient_ImplementsIChatCallback()
    {
        var source = ToolTemplates.RenderClientChatClient();

        Assert.Contains("class ChatClient : IChatCallback", source, StringComparison.Ordinal);
        Assert.Contains("using System.Threading.Tasks;", source, StringComparison.Ordinal);
        Assert.Contains("using Rpc.Generated;", source, StringComparison.Ordinal);
        Assert.Contains("new RpcClient(options, callbacks)", source, StringComparison.Ordinal);
        Assert.Contains("_rpcClient.Api.Shared.Chat", source, StringComparison.Ordinal);
        Assert.Contains("OnMessageReceived?.Invoke", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderChatTemplatesUseSharedContractsChatNamespace()
    {
        var protocols = ToolTemplates.RenderSharedChatProtocols();
        var messages = ToolTemplates.RenderSharedChatMessages();
        var roomActor = ToolTemplates.RenderServerChatRoomActor();
        var rules = ToolTemplates.RenderServerChatRules();
        var service = ToolTemplates.RenderServerChatServiceImpl();
        var hotfix = ToolTemplates.RenderHotfixChatSystem();
        var client = ToolTemplates.RenderClientChatClient();
        var unityUi = ToolTemplates.RenderClientChatUI(CliParser.ParseNewOptions([]));
        var godotScene = ToolTemplates.RenderGodotChatScene(CliParser.ParseNewOptions([]));

        Assert.Contains("namespace Shared.Contracts.Chat", protocols, StringComparison.Ordinal);
        Assert.Contains("namespace Shared.Contracts.Chat", messages, StringComparison.Ordinal);
        Assert.Contains("using Shared.Contracts.Chat;", roomActor, StringComparison.Ordinal);
        Assert.Contains("using Shared.Contracts.Chat;", service, StringComparison.Ordinal);
        Assert.Contains("using Shared.Contracts.Chat;", hotfix, StringComparison.Ordinal);
        Assert.Contains("using Shared.Contracts.Chat;", client, StringComparison.Ordinal);
        Assert.Contains("using Shared.Contracts.Chat;", unityUi, StringComparison.Ordinal);
        Assert.Contains("using Shared.Contracts.Chat;", godotScene, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace Shared.Chat", string.Concat(protocols, messages, roomActor, rules, service, hotfix, client, unityUi, godotScene), StringComparison.Ordinal);
        Assert.DoesNotContain("using Shared.Chat;", string.Concat(protocols, messages, roomActor, rules, service, hotfix, client, unityUi, godotScene), StringComparison.Ordinal);
    }

    [Fact]
    public void RenderClientChatUi_RequiresUiDocument()
    {
        var source = ToolTemplates.RenderClientChatUI(CliParser.ParseNewOptions([]));

        Assert.Contains("RequireComponent(typeof(UIDocument))", source, StringComparison.Ordinal);
        Assert.Contains("new KcpTransport(_serverHost, _serverPort)", source, StringComparison.Ordinal);
        Assert.Contains("new MemoryPackRpcSerializer()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("?.clicked +=", source, StringComparison.Ordinal);
        Assert.Contains("ConcurrentQueue<Action>", source, StringComparison.Ordinal);
        Assert.Contains("client.OnMessageReceived += msg => EnqueueMainThread(() => AppendMessage(msg));", source, StringComparison.Ordinal);
        Assert.Contains("AppendSystemMessage(\"Join the chat before sending.\");", source, StringComparison.Ordinal);
        Assert.Contains("chat-input", source, StringComparison.Ordinal);
        Assert.Contains("message-list", source, StringComparison.Ordinal);
        Assert.Contains("send-button", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderClientChatUi_UsesSelectedTransportAndSerializer()
    {
        var source = ToolTemplates.RenderClientChatUI(new NewCommandOptions(
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

        Assert.Contains("color: rgb(230, 230, 230);", source, StringComparison.Ordinal);
        Assert.Contains(".name-field .unity-text-field__input", source, StringComparison.Ordinal);
        Assert.Contains("color: rgb(245, 245, 245);", source, StringComparison.Ordinal);
        Assert.Contains(".join-button:disabled", source, StringComparison.Ordinal);
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

        Assert.Equal("@import url(\"unity-theme://default\");", source.Trim());
    }

    [Fact]
    public async Task AugmentExistingStarterServerProjectKeepsStarterPingSampleContracts()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "lakona-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var serverDirectory = Path.Combine(projectRoot, "Server", "Server");
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
            var serverDirectory = Path.Combine(projectRoot, "Server", "Server");
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
            Directory.CreateDirectory(Path.Combine(projectRoot, "Server", "Server"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Shared"));
            await File.WriteAllTextAsync(
                Path.Combine(projectRoot, "Server", "Server", "Server.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """,
                TestContext.Current.CancellationToken);

            await new ProjectScaffolder().AugmentProjectWithLakonaGameAsync(projectRoot, CliParser.ParseNewOptions([]));

            var service = await File.ReadAllTextAsync(Path.Combine(projectRoot, "Server", "Server", "Chat", "ChatServiceImpl.cs"), TestContext.Current.CancellationToken);
            var binding = await File.ReadAllTextAsync(Path.Combine(projectRoot, "Server", "Server", "Hosting", "ServiceBindingConfigurator.cs"), TestContext.Current.CancellationToken);
            var actor = await File.ReadAllTextAsync(Path.Combine(projectRoot, "Server", "Server", "Chat", "ChatRoomActor.cs"), TestContext.Current.CancellationToken);
            var rules = await File.ReadAllTextAsync(Path.Combine(projectRoot, "Server", "Server", "Chat", "ChatRules.cs"), TestContext.Current.CancellationToken);
            var hotfix = await File.ReadAllTextAsync(Path.Combine(projectRoot, "Server", "Hotfix", "Chat", "ChatRulesSystem.cs"), TestContext.Current.CancellationToken);

            Assert.Contains("IActorRuntime", service, StringComparison.Ordinal);
            Assert.Contains("AskAsync<ChatRoomActor", service, StringComparison.Ordinal);
            Assert.Contains("class ChatRoomActor : Actor", actor, StringComparison.Ordinal);
            Assert.Contains("FilterMessage", actor, StringComparison.Ordinal);
            Assert.Contains("HotfixDispatch.Invoke", rules, StringComparison.Ordinal);
            Assert.Contains("[HotfixSystemOf(typeof(ChatRuleState))]", hotfix, StringComparison.Ordinal);
            Assert.Contains("ActivatorUtilities.CreateInstance<ChatServiceImpl>(services, callback)", binding, StringComparison.Ordinal);
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
}
