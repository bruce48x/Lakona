using System.Text.Json;
using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Server;
using Xunit;

namespace Lakona.Tool.Tests.Rendering;

public sealed class ServerAppRendererTests
{
    [Fact]
    public void AddFiles_EmitsServerAppProjectProgramAndCompactSettings()
    {
        var plan = Render(Spec(TransportKind.Kcp, SerializerKind.MemoryPack));

        Assert.Contains("<Project Path=\"../Shared/Shared.csproj\" />", AssertPath(plan, "Server/Server.slnx").Content, StringComparison.Ordinal);
        Assert.Contains("<Project Path=\"App/Server.App.csproj\" />", AssertPath(plan, "Server/Server.slnx").Content, StringComparison.Ordinal);

        var project = AssertPath(plan, "Server/App/Server.App.csproj").Content;
        Assert.Contains("<OutputType>Exe</OutputType>", project, StringComparison.Ordinal);
        Assert.Contains("<LakonaRpcGenerateServer>true</LakonaRpcGenerateServer>", project, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Lakona.Game.Server\"", project, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Lakona.Rpc.Transport.Kcp\"", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona.Rpc.Serializer.MemoryPack", project, StringComparison.Ordinal);

        var program = AssertPath(plan, "Server/App/Program.cs").Content;
        Assert.Contains("using Lakona.Game.Server.Hosting;", program, StringComparison.Ordinal);
        Assert.Contains("using Microsoft.Extensions.DependencyInjection;", program, StringComparison.Ordinal);
        Assert.Contains("using Server.App.Chat;", program, StringComparison.Ordinal);
        Assert.Contains("using Server.App.Hosting;", program, StringComparison.Ordinal);
        Assert.Contains("using Lakona.Rpc.Serializer.MemoryPack;", program, StringComparison.Ordinal);
        Assert.Contains("using Lakona.Rpc.Transport.Kcp;", program, StringComparison.Ordinal);
        Assert.Contains("return await LakonaGameServer.RunAsync(args, server => server", program, StringComparison.Ordinal);
        Assert.Contains(".UseTransport(\"kcp\")", program, StringComparison.Ordinal);
        Assert.Contains(".UseSerializer(() => new MemoryPackRpcSerializer())", program, StringComparison.Ordinal);
        Assert.Contains("new KcpConnectionAcceptor(opts.Port, opts.Host)", program, StringComparison.Ordinal);
        Assert.Contains(".AddServices(services => services.AddSingleton<ChatConnectionLifecycle>())", program, StringComparison.Ordinal);
        Assert.Contains(".BindServices(ServiceBindingConfigurator.Bind));", program, StringComparison.Ordinal);
        Assert.DoesNotContain("RpcServerHostBuilder", program, StringComparison.Ordinal);

        var chatRoomActor = AssertPath(plan, "Server/App/Chat/ChatRoomActor.cs").Content;
        Assert.Contains("internal sealed class ChatRoomActor : Actor", chatRoomActor, StringComparison.Ordinal);
        Assert.Contains("internal readonly Dictionary<string, ChatRoomMember> Members", chatRoomActor, StringComparison.Ordinal);
        Assert.Contains("internal readonly Queue<ChatMessage> RecentMessages", chatRoomActor, StringComparison.Ordinal);
        Assert.DoesNotContain("ValueTask<LoginReply> LoginAsync", chatRoomActor, StringComparison.Ordinal);
        Assert.DoesNotContain("void BindChatCallback", chatRoomActor, StringComparison.Ordinal);
        Assert.DoesNotContain("ValueTask SendAsync", chatRoomActor, StringComparison.Ordinal);
        Assert.DoesNotContain("ValueTask.CompletedTask", chatRoomActor, StringComparison.Ordinal);
        Assert.DoesNotContain("ValueTask.FromResult", chatRoomActor, StringComparison.Ordinal);

        var loginProxy = AssertPath(plan, "Server/App/Chat/LoginServiceProxy.cs").Content;
        Assert.Contains("internal sealed class LoginServiceProxy : ILoginService", loginProxy, StringComparison.Ordinal);
        Assert.Contains("IHotfixServiceInvoker", loginProxy, StringComparison.Ordinal);

        var chatProxy = AssertPath(plan, "Server/App/Chat/ChatServiceProxy.cs").Content;
        Assert.Contains("internal sealed class ChatServiceProxy : IChatService", chatProxy, StringComparison.Ordinal);
        Assert.Contains("IHotfixServiceInvoker", chatProxy, StringComparison.Ordinal);

        var lifecycle = AssertPath(plan, "Server/App/Chat/ChatConnectionLifecycle.cs").Content;
        Assert.Contains("internal sealed class ChatConnectionLifecycle", lifecycle, StringComparison.Ordinal);
        Assert.Contains("session.Disconnected", lifecycle, StringComparison.Ordinal);
        Assert.Contains("session.Disconnected += ex =>", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("session.Disconnected += _ =>", lifecycle, StringComparison.Ordinal);
        Assert.Contains("AskAsync<ChatRoomActor", lifecycle, StringComparison.Ordinal);

        var binding = AssertPath(plan, "Server/App/Hosting/ServiceBindingConfigurator.cs").Content;
        Assert.Contains("LoginServiceBinder.BindFactory", binding, StringComparison.Ordinal);
        Assert.Contains("ChatServiceBinder.BindFactory", binding, StringComparison.Ordinal);
        Assert.Contains("new LoginCallbackProxy(session)", binding, StringComparison.Ordinal);
        Assert.Contains("new ChatCallbackProxy(session)", binding, StringComparison.Ordinal);
        Assert.DoesNotContain("Assembly.LoadFrom", binding, StringComparison.Ordinal);

        AssertPath(plan, "Server/App/Properties/AssemblyInfo.cs");

        var appsettings = AssertPath(plan, "Server/App/appsettings.json").Content;
        using var document = JsonDocument.Parse(appsettings);
        var endpoint = document.RootElement.GetProperty("Lakona.Game").GetProperty("Endpoints")[0];
        Assert.Equal("kcp", endpoint.GetProperty("Transport").GetString());
        Assert.Equal("127.0.0.1", endpoint.GetProperty("Host").GetString());
        Assert.Equal(20000, endpoint.GetProperty("Port").GetInt32());
        Assert.DoesNotContain("Enabled", appsettings, StringComparison.Ordinal);
        Assert.DoesNotContain("Bootstrap", appsettings, StringComparison.Ordinal);
    }

    [Fact]
    public void AddFiles_WebSocketSettingsIncludeOnlyPathExtension()
    {
        var plan = Render(Spec(TransportKind.WebSocket, SerializerKind.Json));
        var appsettings = AssertPath(plan, "Server/App/appsettings.json").Content;

        using var document = JsonDocument.Parse(appsettings);
        var endpoint = document.RootElement.GetProperty("Lakona.Game").GetProperty("Endpoints")[0];
        Assert.Equal("websocket", endpoint.GetProperty("Transport").GetString());
        Assert.Equal("/ws", endpoint.GetProperty("Path").GetString());

        var program = AssertPath(plan, "Server/App/Program.cs").Content;
        Assert.Contains("using Lakona.Rpc.Serializer.Json;", program, StringComparison.Ordinal);
        Assert.Contains("using Lakona.Rpc.Transport.WebSocket;", program, StringComparison.Ordinal);
        Assert.Contains(".UseTransport(\"websocket\")", program, StringComparison.Ordinal);
        Assert.Contains(".UseSerializer(() => new JsonRpcSerializer())", program, StringComparison.Ordinal);
        Assert.Contains("WsConnectionAcceptor.CreateAsync(opts.Port, opts.Path, opts.Host)", program, StringComparison.Ordinal);
    }

    [Fact]
    public void AddFiles_EmitsHotfixBuildTagPropsAndImportsIt()
    {
        var plan = Render(Spec(TransportKind.Kcp, SerializerKind.MemoryPack));

        var buildTag = AssertPath(plan, "Server/App/BuildTag.props").Content;
        Assert.Contains("<LakonaHotfixBuildTag>20260612.001</LakonaHotfixBuildTag>", buildTag, StringComparison.Ordinal);

        var project = AssertPath(plan, "Server/App/Server.App.csproj").Content;
        Assert.Contains("<Import Project=\"BuildTag.props\" />", project, StringComparison.Ordinal);
        Assert.Contains("LakonaHotfixBuildTag", AssertPath(plan, "Server/App/Properties/AssemblyInfo.cs").Content, StringComparison.Ordinal);
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
