using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Server;
using Xunit;

namespace Lakona.Tool.Tests.Rendering;

public sealed class HotfixRendererTests
{
    [Fact]
    public void AddFiles_EmitsHotfixProjectAndRuleServices()
    {
        var spec = new LakonaProjectSpecFactory().Create(new NewProjectOptions(
            "MyGame",
            ".",
            ClientEngine.Unity,
            TransportKind.Kcp,
            SerializerKind.MemoryPack,
            PersistenceKind.None,
            NuGetForUnitySource.OpenUpm,
            DeploymentProfile.None));
        var builder = new GenerationPlanBuilder("Root");

        new HotfixRenderer().AddFiles(spec, builder);

        var plan = builder.Build();
        var project = Assert.Single(plan.Files, file => file.RelativePath == "Server/Hotfix/Server.Hotfix.csproj").Content;
        Assert.Contains("Server.Hotfix", project, StringComparison.Ordinal);
        Assert.Contains("..\\App\\Server.App.csproj", project, StringComparison.Ordinal);
        Assert.Contains("<Import Project=\"..\\App\\BuildTag.props\" />", project, StringComparison.Ordinal);

        var loginService = Assert.Single(plan.Files, file => file.RelativePath == "Server/Hotfix/Login/LoginService.cs").Content;
        Assert.Contains("[HotfixService(typeof(ILoginService))]", loginService, StringComparison.Ordinal);
        Assert.Contains("internal sealed class LoginService", loginService, StringComparison.Ordinal);
        Assert.Contains("public static async ValueTask<LoginReply> LoginAsync(HotfixServiceCall<LoginRequest, ILoginCallback> call)", loginService, StringComparison.Ordinal);
        Assert.Contains("call.Services.GetRequiredService<ChatRoomActors>()", loginService, StringComparison.Ordinal);
        Assert.Contains(".Get(ChatRoomIds.Global)", loginService, StringComparison.Ordinal);
        Assert.Contains("new ChatRoomLoginRequest", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain(".AskAsync", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Actors is node-local", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("var starterNodeLocalActors = call.Actors;", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("starterLocalActors", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("var localActors = call.Actors;", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("localActors.AskAsync", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Actors.AskAsync", loginService, StringComparison.Ordinal);
        Assert.Contains("await call.GameServer.StartSessionAsync", loginService, StringComparison.Ordinal);
        Assert.Contains("return reply;", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("reply.Session", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("LoginServiceCall", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateLocalAsync", loginService, StringComparison.Ordinal);

        var chatService = Assert.Single(plan.Files, file => file.RelativePath == "Server/Hotfix/Chat/ChatService.cs").Content;
        Assert.Contains("[HotfixService(typeof(IChatService))]", chatService, StringComparison.Ordinal);
        Assert.Contains("internal sealed class ChatService", chatService, StringComparison.Ordinal);
        Assert.Contains("public static async ValueTask BindAsync(HotfixServiceCall<ChatBindRequest, IChatCallback> call)", chatService, StringComparison.Ordinal);
        Assert.Contains("await call.GameServer.BindCurrentSessionAsync", chatService, StringComparison.Ordinal);
        Assert.Contains("call.ConnectionId", chatService, StringComparison.Ordinal);
        Assert.Contains("call.Callback", chatService, StringComparison.Ordinal);
        Assert.Contains("call.Services.GetRequiredService<ChatRoomActors>()", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("var localActors = call.Actors;", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Request.Session", chatService, StringComparison.Ordinal);
        Assert.Contains("public static async ValueTask SendAsync(HotfixServiceCall<ChatSendRequest, IChatCallback> call)", chatService, StringComparison.Ordinal);
        Assert.Contains(".Get(ChatRoomIds.Global)", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain(".AskAsync", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("starterLocalActors", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("localActors.AskAsync", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Actors.AskAsync", chatService, StringComparison.Ordinal);
        Assert.Contains("badword", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatServiceCall", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateLocalAsync", chatService, StringComparison.Ordinal);

        var behavior = Assert.Single(plan.Files, file => file.RelativePath == "Server/Hotfix/Chat/ChatRoomBehavior.cs").Content;
        Assert.Contains("[HotfixBehaviorOf(typeof(ChatRoomActor))]", behavior, StringComparison.Ordinal);
        Assert.Contains("public static ValueTask<LoginReply> LoginAsync", behavior, StringComparison.Ordinal);
        Assert.Contains("ChatRoomLoginRequest request", behavior, StringComparison.Ordinal);
        Assert.Contains("public static ValueTask LeaveAsync", behavior, StringComparison.Ordinal);
        Assert.Contains("ChatRoomLeaveRequest request", behavior, StringComparison.Ordinal);

        var feature = Assert.Single(plan.Files, file => file.RelativePath == "Server/Hotfix/Features/ChatFeature.cs").Content;
        Assert.Contains("[HotfixFeature(\"chat\")]", feature, StringComparison.Ordinal);
        Assert.Contains("public sealed class ChatFeature : HotfixGameFeature", feature, StringComparison.Ordinal);
        Assert.Contains("context.EnsureLocalActor<ChatRoomActor>(ChatRoomIds.Global);", feature, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateLocalAsync", feature, StringComparison.Ordinal);
        Assert.DoesNotContain("ScheduleActorTick", feature, StringComparison.Ordinal);

        var lifecycle = Assert.Single(plan.Files, file => file.RelativePath == "Server/Hotfix/Chat/ChatSessionLifecycle.cs").Content;
        Assert.Contains("[HotfixLifecycle(typeof(IGameSessionLifecycle))]", lifecycle, StringComparison.Ordinal);
        Assert.Contains("internal sealed class ChatSessionLifecycle", lifecycle, StringComparison.Ordinal);
        Assert.Contains("public static ValueTask SessionDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call)", lifecycle, StringComparison.Ordinal);
        Assert.Contains("public static async ValueTask SessionExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call)", lifecycle, StringComparison.Ordinal);
        Assert.Contains("call.Services.GetRequiredService<ChatRoomActors>()", lifecycle, StringComparison.Ordinal);
        Assert.Contains(".Get(ChatRoomIds.Global)", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain(".AskAsync", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("starterLocalActors", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("var localActors = call.Actors;", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("localActors.AskAsync", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Actors.AskAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("new ChatRoomLeaveRequest", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("LifecycleService", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("IChatRuntimeService", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("HotfixDispatch", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateLocalAsync", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain(plan.Files, file => file.RelativePath == "Server/Hotfix/Chat/ChatRuntimeService.cs");

        Assert.DoesNotContain(plan.Files, file => file.Content.Contains("static event", StringComparison.OrdinalIgnoreCase));
    }
}
