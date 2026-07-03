using System.Text.Json;
using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Server;
using Xunit;

namespace Lakona.Tool.Tests.Rendering;

public sealed class ServerAppRendererTests
{
    private static readonly string ForbiddenGeneratedGlueFile = string.Concat("Generated", "Service", "Endpoints");
    private static readonly string ForbiddenHotfixMarker = string.Concat("Hotfix", "Rpc", "Service");
    private static readonly string ForbiddenGameEndpointType = string.Concat("Game", "Endpoint", "Name");
    private static readonly string ForbiddenSessionEndpointType = string.Concat("Session", "Endpoint", "Key");
    private static readonly string ForbiddenCleanupOption = string.Concat("Disconnected", "Endpoint", "Retention");
    private static readonly string ForbiddenEndpointHookPrefix = string.Concat("On", "Endpoint");
    private static readonly string ForbiddenAppHotfixDirectory = string.Concat("Server/App/Hot", "fix/");
    private static readonly string ForbiddenAppEventAdapterName = string.Concat("Hotfix", "Runtime", "Events");
    private static readonly string ForbiddenEventAdapterSuffix = string.Concat("Runtime", "Events");
    private static readonly string ForbiddenRoomLoopName = string.Concat("Room", "Runtime");
    private static readonly string ForbiddenMatchLoopHostName = string.Concat("Matchmaking", "Hosted", "Service");
    private static readonly string ForbiddenDispatchCall = string.Concat("HotfixDispatch", ".Invoke");
    private static readonly string ForbiddenContractAttribute = string.Concat("Hotfix", "Actor", "Contract");
    private static readonly string ForbiddenChatRoomContractInterface = string.Concat("IChatRoom", "Actor", "Contract");
    private static readonly string ForbiddenStableActorRefsProperty = string.Concat("LakonaHotfixGenerateStable", "ActorRefs");

    [Fact]
    public void AddFiles_EmitsServerAppProjectProgramAndCompactSettings()
    {
        var plan = Render(Spec(TransportKind.Kcp, SerializerKind.MemoryPack));

        Assert.Contains("<Project Path=\"../Shared/Shared.csproj\" />", AssertPath(plan, "Server/Server.slnx").Content, StringComparison.Ordinal);
        Assert.Contains("<Project Path=\"App/Server.App.csproj\" />", AssertPath(plan, "Server/Server.slnx").Content, StringComparison.Ordinal);

        var project = AssertPath(plan, "Server/App/Server.App.csproj").Content;
        Assert.Contains("<OutputType>Exe</OutputType>", project, StringComparison.Ordinal);
        Assert.Contains("<LakonaRpcGenerateServer>true</LakonaRpcGenerateServer>", project, StringComparison.Ordinal);
        Assert.Contains("<CompilerVisibleProperty Include=\"LakonaRpcGenerateServer\" />", project, StringComparison.Ordinal);
        Assert.Contains("<CompilerVisibleProperty Include=\"LakonaRpcServerGeneratedNamespace\" />", project, StringComparison.Ordinal);
        Assert.Contains("<LakonaHotfixGenerateStableRpcServices>true</LakonaHotfixGenerateStableRpcServices>", project, StringComparison.Ordinal);
        Assert.Contains("<CompilerVisibleProperty Include=\"LakonaHotfixGenerateStableRpcServices\" />", project, StringComparison.Ordinal);
        Assert.DoesNotContain(ForbiddenStableActorRefsProperty, project, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Lakona.Game.Server\"", project, StringComparison.Ordinal);
        Assert.DoesNotContain("<PackageReference Include=\"Lakona.Rpc.Transport.Kcp\"", project, StringComparison.Ordinal);
        Assert.DoesNotContain("<PackageReference Include=\"Lakona.Rpc.Transport.WebSocket\"", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona.Rpc.Serializer.MemoryPack", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona.Rpc.Serializer.Json", project, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Lakona.Game.Server.Hotfix.Abstractions\"", project, StringComparison.Ordinal);

        var program = AssertPath(plan, "Server/App/Program.cs").Content;
        Assert.Equal("using Lakona.Game.Server.Hosting;\n\nreturn await LakonaGameServer.RunAsync(args);", program);
        Assert.Contains("using Lakona.Game.Server.Hosting;", program, StringComparison.Ordinal);
        Assert.DoesNotContain("using Lakona.Game.Server.Sessions;", program, StringComparison.Ordinal);
        Assert.DoesNotContain("using Microsoft.Extensions.DependencyInjection;", program, StringComparison.Ordinal);
        Assert.DoesNotContain("using Lakona.Game.Server.Hotfix.Abstractions;", program, StringComparison.Ordinal);
        Assert.DoesNotContain("using Server.App.Hosting;", program, StringComparison.Ordinal);
        Assert.DoesNotContain("using Server.App.Hotfix;", program, StringComparison.Ordinal);
        Assert.Contains("return await LakonaGameServer.RunAsync(args);", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona.Rpc.Serializer", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona.Rpc.Transport", program, StringComparison.Ordinal);
        Assert.DoesNotContain(".UseTransport(", program, StringComparison.Ordinal);
        Assert.DoesNotContain(".UseSerializer(", program, StringComparison.Ordinal);
        Assert.DoesNotContain(".UseAcceptor(", program, StringComparison.Ordinal);
        Assert.DoesNotContain("services.AddLakonaGameSessionHotfixLifecycle", program, StringComparison.Ordinal);
        Assert.DoesNotContain("services.AddSingleton<ChatHotfixRuntimeEvents>();", program, StringComparison.Ordinal);
        Assert.DoesNotContain("services.AddSingleton<IHotfixRequiredServiceContracts, ChatRuntimeRequiredServiceContracts>();", program, StringComparison.Ordinal);
        Assert.DoesNotContain("services.AddSingleton<IGameSessionLifecycleHandler, ChatSessionLifecycleBridge>();", program, StringComparison.Ordinal);
        Assert.DoesNotContain("using Server.App.Lifecycle;", program, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatPresenceLifecycleHandler", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddLakonaGameServerSessionCleanup", program, StringComparison.Ordinal);
        Assert.DoesNotContain("DisconnectedSessionRetention = TimeSpan", program, StringComparison.Ordinal);
        Assert.DoesNotContain(ForbiddenCleanupOption, program, StringComparison.Ordinal);
        Assert.DoesNotContain("UseGeneratedHotfixServices", program, StringComparison.Ordinal);
        Assert.DoesNotContain("RpcServerHostBuilder", program, StringComparison.Ordinal);
        Assert.DoesNotContain(".AddServices((services, configuration) =>", program, StringComparison.Ordinal);
        Assert.DoesNotContain("services.AddLakonaGame(configuration)", program, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigureFeatures(", program, StringComparison.Ordinal);
        Assert.DoesNotContain(".Feature<", program, StringComparison.Ordinal);

        var chatRoomActor = AssertPath(plan, "Server/App/Chat/ChatRoomActor.cs").Content;
        Assert.Contains("internal sealed class ChatRoomActor : Actor<string>", chatRoomActor, StringComparison.Ordinal);
        Assert.Contains("internal readonly Dictionary<string, ChatRoomMember> Members", chatRoomActor, StringComparison.Ordinal);
        Assert.Contains("internal readonly Queue<ChatMessage> RecentMessages", chatRoomActor, StringComparison.Ordinal);
        Assert.DoesNotContain("ValueTask<LoginReply> LoginAsync", chatRoomActor, StringComparison.Ordinal);
        Assert.DoesNotContain("void BindChatCallback", chatRoomActor, StringComparison.Ordinal);
        Assert.DoesNotContain("ValueTask SendAsync", chatRoomActor, StringComparison.Ordinal);
        Assert.DoesNotContain("ValueTask.CompletedTask", chatRoomActor, StringComparison.Ordinal);
        Assert.DoesNotContain("ValueTask.FromResult", chatRoomActor, StringComparison.Ordinal);

        Assert.DoesNotContain(plan.Files, file => file.RelativePath == "Server/App/Chat/ChatRoomActorContracts.cs");
        var chatRoomMessages = AssertPath(plan, "Server/App/Chat/ChatRoomMessages.cs").Content;
        Assert.Contains("public static class ChatRoomIds", chatRoomMessages, StringComparison.Ordinal);
        Assert.Contains("public sealed class ChatRoomLoginRequest", chatRoomMessages, StringComparison.Ordinal);
        Assert.Contains("public sealed class ChatRoomBindRequest", chatRoomMessages, StringComparison.Ordinal);
        Assert.Contains("public sealed class ChatRoomSendRequest", chatRoomMessages, StringComparison.Ordinal);
        Assert.Contains("public sealed class ChatRoomLeaveRequest", chatRoomMessages, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona.Game.Server.Hotfix.Abstractions", chatRoomMessages, StringComparison.Ordinal);
        Assert.DoesNotContain(ForbiddenContractAttribute, chatRoomMessages, StringComparison.Ordinal);
        Assert.DoesNotContain(ForbiddenChatRoomContractInterface, chatRoomMessages, StringComparison.Ordinal);

        Assert.DoesNotContain(plan.Files, file => file.RelativePath == "Server/App/Chat/LoginServiceProxy.cs");
        Assert.DoesNotContain(plan.Files, file => file.RelativePath == "Server/App/Chat/ChatServiceProxy.cs");
        Assert.DoesNotContain(plan.Files, file => file.RelativePath == "Server/App/Hosting/ServiceBindingConfigurator.cs");
        Assert.DoesNotContain(plan.Files, file => file.RelativePath == "Server/App/Chat/ChatConnectionLifecycle.cs");
        Assert.DoesNotContain(plan.Files, file => file.RelativePath.StartsWith(ForbiddenAppHotfixDirectory, StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.RelativePath == "Server/App/Hosting/ChatSessionLifecycleBridge.cs");

        Assert.DoesNotContain(plan.Files, file => file.RelativePath == $"Server/App/Services/{ForbiddenGeneratedGlueFile}.cs");
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains(ForbiddenGeneratedGlueFile, StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains(ForbiddenHotfixMarker, StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains("IChatRuntimeService", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains("ChatRuntimeContracts", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains(ForbiddenAppEventAdapterName, StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains(ForbiddenEventAdapterSuffix, StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains(ForbiddenRoomLoopName, StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains(ForbiddenMatchLoopHostName, StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains("IGameSessionLifecycleHandler", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains(ForbiddenContractAttribute, StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains(ForbiddenChatRoomContractInterface, StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains(ForbiddenStableActorRefsProperty, StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains("ChatSessionLifecycleBridge", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains("ChatHotfixRuntimeEvents", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains("EndpointName", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains(ForbiddenGameEndpointType, StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains(ForbiddenSessionEndpointType, StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains(ForbiddenDispatchCall, StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains("namespace Server.App.Lifecycle", StringComparison.Ordinal));

        Assert.DoesNotContain(plan.Files, file => file.RelativePath == "Server/App/Lifecycle/ChatPresenceLifecycleHandler.cs");
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains(ForbiddenEndpointHookPrefix, StringComparison.Ordinal));

        Assert.DoesNotContain(plan.Files, file => file.Content.Contains("class LoginServiceProxy", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains("class ChatServiceProxy", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains("ServiceBindingConfigurator", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains("RpcSession.Disconnected +=", StringComparison.Ordinal));

        Assert.DoesNotContain(plan.Files, file => file.RelativePath == "Server/App/Properties/AssemblyInfo.cs");

        var appsettings = AssertPath(plan, "Server/App/appsettings.json").Content;
        using var document = JsonDocument.Parse(appsettings);
        Assert.Contains("\"Lakona\"", appsettings, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Lakona.Game\"", appsettings, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona:Cluster:Services", appsettings, StringComparison.Ordinal);
        Assert.Contains("\"RpcServices\"", appsettings, StringComparison.Ordinal);

        var lakona = document.RootElement.GetProperty("Lakona");
        Assert.False(lakona.TryGetProperty("Feature", out _));
        Assert.DoesNotContain(
            lakona.EnumerateObject(),
            property => string.Equals(property.Name, "Cluster", StringComparison.OrdinalIgnoreCase));

        var endpoint = lakona.GetProperty("Endpoints")[0];
        Assert.Equal("kcp", endpoint.GetProperty("Transport").GetString());
        Assert.Equal("memorypack", endpoint.GetProperty("Serializer").GetString());
        Assert.Equal("127.0.0.1", endpoint.GetProperty("Host").GetString());
        Assert.Equal(20000, endpoint.GetProperty("Port").GetInt32());
        Assert.Equal(new[] { "login", "chat" }, endpoint.GetProperty("RpcServices").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.False(endpoint.TryGetProperty("Name", out _));
        Assert.False(endpoint.TryGetProperty("Path", out _));
        var cleanup = lakona
            .GetProperty("Sessions")
            .GetProperty("Cleanup");
        Assert.Equal(30, cleanup.GetProperty("DisconnectedRetentionSeconds").GetInt32());
        Assert.False(cleanup.TryGetProperty("Enabled", out _));
        Assert.DoesNotContain("Enabled", appsettings, StringComparison.Ordinal);
        Assert.DoesNotContain("Bootstrap", appsettings, StringComparison.Ordinal);
    }

    [Fact]
    public void AddFiles_WebSocketSettingsIncludeOnlyPathExtension()
    {
        var plan = Render(Spec(TransportKind.WebSocket, SerializerKind.Json));
        var appsettings = AssertPath(plan, "Server/App/appsettings.json").Content;

        using var document = JsonDocument.Parse(appsettings);
        var endpoint = document.RootElement.GetProperty("Lakona").GetProperty("Endpoints")[0];
        Assert.Equal("websocket", endpoint.GetProperty("Transport").GetString());
        Assert.Equal("json", endpoint.GetProperty("Serializer").GetString());
        Assert.Equal("/ws", endpoint.GetProperty("Path").GetString());
        Assert.Equal(new[] { "login", "chat" }, endpoint.GetProperty("RpcServices").EnumerateArray().Select(item => item.GetString()).ToArray());

        var program = AssertPath(plan, "Server/App/Program.cs").Content;
        Assert.DoesNotContain("Lakona.Rpc.Serializer", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona.Rpc.Transport", program, StringComparison.Ordinal);
        Assert.DoesNotContain(".UseTransport(", program, StringComparison.Ordinal);
        Assert.DoesNotContain(".UseSerializer(", program, StringComparison.Ordinal);
        Assert.DoesNotContain(".UseAcceptor(", program, StringComparison.Ordinal);
    }

    [Fact]
    public void AddFiles_EmitsHotfixBuildTagPropsAndImportsIt()
    {
        var plan = Render(Spec(TransportKind.Kcp, SerializerKind.MemoryPack));

        var buildTag = AssertPath(plan, "Server/App/BuildTag.props").Content;
        Assert.Contains("<LakonaHotfixBuildTag>20260629.001</LakonaHotfixBuildTag>", buildTag, StringComparison.Ordinal);

        var project = AssertPath(plan, "Server/App/Server.App.csproj").Content;
        Assert.Contains("<Import Project=\"BuildTag.props\" />", project, StringComparison.Ordinal);
        Assert.Contains("<AssemblyAttribute Include=\"System.Reflection.AssemblyMetadataAttribute\">", project, StringComparison.Ordinal);
        Assert.Contains("<_Parameter1>LakonaHotfixBuildTag</_Parameter1>", project, StringComparison.Ordinal);
        Assert.Contains("<_Parameter2>$(LakonaHotfixBuildTag)</_Parameter2>", project, StringComparison.Ordinal);
        Assert.Contains("<AssemblyAttribute Include=\"System.Runtime.CompilerServices.InternalsVisibleToAttribute\">", project, StringComparison.Ordinal);
        Assert.Contains("<_Parameter1>Server.Hotfix</_Parameter1>", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Server/App/Properties/AssemblyInfo.cs", string.Join('\n', plan.Files.Select(file => file.RelativePath)), StringComparison.Ordinal);
    }

    private static GenerationPlan Render(LakonaProjectSpec spec)
    {
        var builder = new GenerationPlanBuilder("Root");
        new ServerAppRenderer().AddFiles(spec, builder);
        return builder.Build();
    }

    private static LakonaProjectSpec Spec(TransportKind transport, SerializerKind serializer)
    {
        return new LakonaProjectSpecFactory().Create(new NewProjectOptions(
            "MyGame",
            ".",
            ClientEngine.Unity,
            transport,
            serializer,
            PersistenceKind.None,
            NuGetForUnitySource.OpenUpm,
            DeploymentProfile.None));
    }

    private static GeneratedFile AssertPath(GenerationPlan plan, string relativePath)
    {
        return Assert.Single(plan.Files, file => file.RelativePath == relativePath);
    }
}
