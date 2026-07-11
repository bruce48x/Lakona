using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Server;
using Xunit;

namespace Lakona.Tool.Tests.Rendering;

public sealed class HotfixRendererTests
{
    private static readonly string ForbiddenStableActorRefsProperty = string.Concat("LakonaHotfixGenerateStable", "ActorRefs");

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
        Assert.Contains("<LakonaHotfixGenerateStableRpcServices>false</LakonaHotfixGenerateStableRpcServices>", project, StringComparison.Ordinal);
        Assert.Contains("<CompilerVisibleProperty Include=\"LakonaHotfixGenerateStableRpcServices\" />", project, StringComparison.Ordinal);
        Assert.DoesNotContain(ForbiddenStableActorRefsProperty, project, StringComparison.Ordinal);
        Assert.DoesNotContain("ReferenceOutputAssembly=\"false\" />", project, StringComparison.Ordinal);
        Assert.Contains("..\\App\\Server.App.csproj", project, StringComparison.Ordinal);
        Assert.Contains("<Import Project=\"..\\App\\BuildTag.props\" />", project, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Lakona.Game.Server.Hotfix.Generators\"", project, StringComparison.Ordinal);
        Assert.Contains("OutputItemType=\"Analyzer\"", project, StringComparison.Ordinal);

        var loginService = Assert.Single(plan.Files, file => file.RelativePath == "Server/Hotfix/Login/LoginService.cs").Content;
        Assert.Contains("[HotfixService(typeof(ILoginService))]", loginService, StringComparison.Ordinal);
        Assert.Contains("internal sealed class LoginService", loginService, StringComparison.Ordinal);
        Assert.Contains("public LoginService(ChatRoomActors rooms, ILakonaGameServer gameServer)", loginService, StringComparison.Ordinal);
        Assert.Contains("private readonly ChatRoomActors _rooms;", loginService, StringComparison.Ordinal);
        Assert.Contains("public async ValueTask<LoginReply> LoginAsync(HotfixServiceCall<LoginRequest, ILoginCallback> call)", loginService, StringComparison.Ordinal);
        Assert.Contains("await _rooms", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Services.GetRequiredService<ChatRoomActors>()", loginService, StringComparison.Ordinal);
        Assert.Contains(".Startup(ChatRoomIds.Global)", loginService, StringComparison.Ordinal);
        Assert.Contains(".CallAsync(", loginService, StringComparison.Ordinal);
        Assert.Contains("new ChatRoomLoginRequest", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain(".AskAsync", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Actors is node-local", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("var starterNodeLocalActors = call.Actors;", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("starterLocalActors", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("var localActors = call.Actors;", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("localActors.AskAsync", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Actors.AskAsync", loginService, StringComparison.Ordinal);
        Assert.Contains("await _gameServer.StartSessionAsync", loginService, StringComparison.Ordinal);
        Assert.Contains("return reply;", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("reply.Session", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("LoginServiceCall", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Create", "Local", "Async"), loginService, StringComparison.Ordinal);

        var chatService = Assert.Single(plan.Files, file => file.RelativePath == "Server/Hotfix/Chat/ChatService.cs").Content;
        Assert.Contains("[HotfixService(typeof(IChatService))]", chatService, StringComparison.Ordinal);
        Assert.Contains("internal sealed class ChatService", chatService, StringComparison.Ordinal);
        Assert.Contains("public ChatService(ChatRoomActors rooms, ILakonaGameServer gameServer, ILogger<ChatService> logger)", chatService, StringComparison.Ordinal);
        Assert.Contains("private readonly ChatRoomActors _rooms;", chatService, StringComparison.Ordinal);
        Assert.Contains("public async ValueTask BindAsync(HotfixServiceCall<ChatBindRequest, IChatCallback> call)", chatService, StringComparison.Ordinal);
        Assert.Contains("await _gameServer.BindCurrentSessionAsync", chatService, StringComparison.Ordinal);
        Assert.Contains("call.ConnectionId", chatService, StringComparison.Ordinal);
        Assert.Contains("call.Callback", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Services.GetRequiredService<ChatRoomActors>()", chatService, StringComparison.Ordinal);
        Assert.Contains("public async ValueTask SendAsync(HotfixServiceCall<ChatSendRequest, IChatCallback> call)", chatService, StringComparison.Ordinal);
        Assert.Contains(".Startup(ChatRoomIds.Global)", chatService, StringComparison.Ordinal);
        Assert.Contains("ChatRoomBehavior.SendAsync", chatService, StringComparison.Ordinal);
        Assert.Contains("ChatRoomBehavior.BindChatAsync", chatService, StringComparison.Ordinal);
        Assert.Contains("CancellationToken.None", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain(".AskAsync", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("starterLocalActors", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("localActors.AskAsync", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Actors.AskAsync", chatService, StringComparison.Ordinal);
        Assert.Contains("badword", chatService, StringComparison.Ordinal);
        Assert.Contains("var text = call.Request.Text ?? \"\";", chatService, StringComparison.Ordinal);
        Assert.Contains("_logger.LogInformation(\"Sending {CharacterCount} characters\", text.Length);", chatService, StringComparison.Ordinal);
        Assert.Contains("await BindChatCallbackAsync(call.ConnectionId, call.Callback);", chatService, StringComparison.Ordinal);
        Assert.Contains("private async ValueTask BindChatCallbackAsync(string connectionId, IChatCallback callback)", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Request.Text.Length", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("FilterMessage(call.Request.Text ?? \"\")", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatServiceCall", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Create", "Local", "Async"), chatService, StringComparison.Ordinal);

        var behavior = Assert.Single(plan.Files, file => file.RelativePath == "Server/Hotfix/Chat/ChatRoomBehavior.cs").Content;
        Assert.Contains("[HotfixBehaviorOf(typeof(ChatRoomActor))]", behavior, StringComparison.Ordinal);
        Assert.Contains("internal static partial class ChatRoomBehavior", behavior, StringComparison.Ordinal);
        Assert.Contains("public static ValueTask<LoginReply> LoginAsync", behavior, StringComparison.Ordinal);
        Assert.Contains("ChatRoomLoginRequest request", behavior, StringComparison.Ordinal);
        Assert.Contains("public static ValueTask LeaveAsync", behavior, StringComparison.Ordinal);
        Assert.Contains("ChatRoomLeaveRequest request", behavior, StringComparison.Ordinal);
        Assert.Contains("Callback exceptions are ignored so one stale client does not prevent other clients from receiving room events.", behavior, StringComparison.Ordinal);
        Assert.Contains("catch (Exception)", behavior, StringComparison.Ordinal);

        var startup = Assert.Single(plan.Files, file => file.RelativePath == "Server/Hotfix/HotfixStartup.cs").Content;
        var oldActorTickType = string.Concat("Hotfix", "ActorTick");
        var oldActorTickSchedule = string.Concat("Schedule", "ActorTick");
        var oldActiveActorTickSchedule = string.Concat("Schedule", "Active", "ActorTicks");
        var oldTimerRegistrationApi = string.Concat("Register", "Timer");
        var publicActorTimerApi = string.Concat("ActorContext.", oldTimerRegistrationApi);
        Assert.Contains("public static class HotfixStartup", startup, StringComparison.Ordinal);
        Assert.Contains("[HotfixStartup]", startup, StringComparison.Ordinal);
        Assert.Contains("[HotfixConfigureServices]", startup, StringComparison.Ordinal);
        Assert.Contains("[HotfixConfigureActors]", startup, StringComparison.Ordinal);
        Assert.Contains("ConfigureServices(IServiceCollection services)", startup, StringComparison.Ordinal);
        Assert.Contains("ConfigureActors(ActorHostBuilder actors)", startup, StringComparison.Ordinal);
        Assert.Contains("actors.RegisterStartup<ChatRoomActor, string>", startup, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Hotfix", "Fea", "ture"), startup, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("HotfixGame", "Fea", "ture"), startup, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("I", "Fea", "tureMessageHandler"), startup, StringComparison.Ordinal);
        Assert.DoesNotContain(".GetRequiredService<ActorHosting>()", startup, StringComparison.Ordinal);
        Assert.DoesNotContain(".CreateAsync<ChatRoomActor>", startup, StringComparison.Ordinal);
        Assert.DoesNotContain(".DestroyAsync<ChatRoomActor>", startup, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Ensure", "Local", "Actor"), startup, StringComparison.Ordinal);
        Assert.DoesNotContain("LakonaTimer", startup, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Create", "Local", "Async"), startup, StringComparison.Ordinal);
        Assert.DoesNotContain(oldActorTickSchedule, startup, StringComparison.Ordinal);
        Assert.DoesNotContain(oldActorTickType, startup, StringComparison.Ordinal);
        Assert.DoesNotContain(oldTimerRegistrationApi, startup, StringComparison.Ordinal);
        Assert.DoesNotContain(publicActorTimerApi, startup, StringComparison.Ordinal);

        var lifecycle = Assert.Single(plan.Files, file => file.RelativePath == "Server/Hotfix/Chat/ChatSessionLifecycle.cs").Content;
        Assert.Contains("[HotfixLifecycle(typeof(IGameSessionLifecycle))]", lifecycle, StringComparison.Ordinal);
        Assert.Contains("internal sealed class ChatSessionLifecycle", lifecycle, StringComparison.Ordinal);
        Assert.Contains("public ChatSessionLifecycle(ChatRoomActors rooms)", lifecycle, StringComparison.Ordinal);
        Assert.Contains("public ValueTask SessionDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call)", lifecycle, StringComparison.Ordinal);
        Assert.Contains("Disconnected sessions stay in the room during the retention window so a client can reconnect without flickering presence.", lifecycle, StringComparison.Ordinal);
        Assert.Contains("public async ValueTask SessionExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call)", lifecycle, StringComparison.Ordinal);
        Assert.Contains("private readonly ChatRoomActors _rooms;", lifecycle, StringComparison.Ordinal);
        Assert.Contains(".Startup(ChatRoomIds.Global)", lifecycle, StringComparison.Ordinal);
        Assert.Contains("ChatRoomBehavior.LeaveAsync", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain(".AskAsync", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("starterLocalActors", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("var localActors = call.Actors;", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("localActors.AskAsync", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Actors.AskAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("new ChatRoomLeaveRequest", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("LifecycleService", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("IChatRuntimeService", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("HotfixDispatch", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Create", "Local", "Async"), lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain(oldActorTickType, lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain(oldTimerRegistrationApi, lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain(plan.Files, file => file.RelativePath == "Server/Hotfix/Chat/ChatRuntimeService.cs");

        Assert.DoesNotContain(plan.Files, file => file.Content.Contains("static event", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains(oldActorTickType, StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains(oldTimerRegistrationApi, StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains(oldActiveActorTickSchedule, StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains(oldActorTickSchedule, StringComparison.Ordinal));
    }
}
