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
        Assert.Contains("<LakonaHotfixGenerateStableRpcServices>false</LakonaHotfixGenerateStableRpcServices>", project, StringComparison.Ordinal);
        Assert.Contains("<LakonaHotfixGenerateStableActorRefs>false</LakonaHotfixGenerateStableActorRefs>", project, StringComparison.Ordinal);
        Assert.Contains("<CompilerVisibleProperty Include=\"LakonaHotfixGenerateStableRpcServices\" />", project, StringComparison.Ordinal);
        Assert.Contains("<CompilerVisibleProperty Include=\"LakonaHotfixGenerateStableActorRefs\" />", project, StringComparison.Ordinal);
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
        Assert.Contains(".Get(ChatRoomIds.Global)", loginService, StringComparison.Ordinal);
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
        Assert.Contains(".Get(ChatRoomIds.Global)", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain(".AskAsync", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("starterLocalActors", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("localActors.AskAsync", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Actors.AskAsync", chatService, StringComparison.Ordinal);
        Assert.Contains("badword", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatServiceCall", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Create", "Local", "Async"), chatService, StringComparison.Ordinal);

        var behavior = Assert.Single(plan.Files, file => file.RelativePath == "Server/Hotfix/Chat/ChatRoomBehavior.cs").Content;
        Assert.Contains("[HotfixBehaviorOf(typeof(ChatRoomActor))]", behavior, StringComparison.Ordinal);
        Assert.Contains("internal static partial class ChatRoomBehavior", behavior, StringComparison.Ordinal);
        Assert.Contains("public static ValueTask<LoginReply> LoginAsync", behavior, StringComparison.Ordinal);
        Assert.Contains("ChatRoomLoginRequest request", behavior, StringComparison.Ordinal);
        Assert.Contains("public static ValueTask LeaveAsync", behavior, StringComparison.Ordinal);
        Assert.Contains("ChatRoomLeaveRequest request", behavior, StringComparison.Ordinal);

        var feature = Assert.Single(plan.Files, file => file.RelativePath == "Server/Hotfix/Features/ChatFeature.cs").Content;
        var oldActorTickType = string.Concat("Hotfix", "ActorTick");
        var oldActorTickSchedule = string.Concat("Schedule", "ActorTick");
        var oldActiveActorTickSchedule = string.Concat("Schedule", "Active", "ActorTicks");
        var oldTimerRegistrationApi = string.Concat("Register", "Timer");
        var publicActorTimerApi = string.Concat("ActorContext.", oldTimerRegistrationApi);
        Assert.Contains("[HotfixFeature(\"chat\")]", feature, StringComparison.Ordinal);
        Assert.Contains("public sealed class ChatFeature : HotfixGameFeature", feature, StringComparison.Ordinal);
        Assert.Contains("public static void Configure(HotfixFeatureContext context)", feature, StringComparison.Ordinal);
        Assert.DoesNotContain("public override void Configure", feature, StringComparison.Ordinal);
        Assert.DoesNotContain("IFeatureMessageHandler", feature, StringComparison.Ordinal);
        Assert.Contains(".GetRequiredService<ActorHosting>()", feature, StringComparison.Ordinal);
        Assert.Contains(".EnsureAsync<ChatRoomActor>(ActorId.From(ChatRoomIds.Global), call.CancellationToken)", feature, StringComparison.Ordinal);
        Assert.Contains(".DestroyAsync<ChatRoomActor>(ActorId.From(actorId), CancellationToken.None)", feature, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Ensure", "Local", "Actor"), feature, StringComparison.Ordinal);
        Assert.DoesNotContain("LakonaTimer", feature, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Create", "Local", "Async"), feature, StringComparison.Ordinal);
        Assert.DoesNotContain(oldActorTickSchedule, feature, StringComparison.Ordinal);
        Assert.DoesNotContain(oldActorTickType, feature, StringComparison.Ordinal);
        Assert.DoesNotContain(oldTimerRegistrationApi, feature, StringComparison.Ordinal);
        Assert.DoesNotContain(publicActorTimerApi, feature, StringComparison.Ordinal);

        var lifecycle = Assert.Single(plan.Files, file => file.RelativePath == "Server/Hotfix/Chat/ChatSessionLifecycle.cs").Content;
        Assert.Contains("[HotfixLifecycle(typeof(IGameSessionLifecycle))]", lifecycle, StringComparison.Ordinal);
        Assert.Contains("internal sealed class ChatSessionLifecycle", lifecycle, StringComparison.Ordinal);
        Assert.Contains("public ChatSessionLifecycle(ChatRoomActors rooms)", lifecycle, StringComparison.Ordinal);
        Assert.Contains("public ValueTask SessionDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call)", lifecycle, StringComparison.Ordinal);
        Assert.Contains("public async ValueTask SessionExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call)", lifecycle, StringComparison.Ordinal);
        Assert.Contains("private readonly ChatRoomActors _rooms;", lifecycle, StringComparison.Ordinal);
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
