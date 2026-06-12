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
        Assert.Contains("public static ValueTask<LoginReply> LoginAsync(HotfixServiceCall<LoginRequest, ILoginCallback> call)", loginService, StringComparison.Ordinal);
        Assert.Contains("return call.Actors.AskAsync<ChatRoomActor, LoginReply>", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("LoginServiceCall", loginService, StringComparison.Ordinal);

        var chatService = Assert.Single(plan.Files, file => file.RelativePath == "Server/Hotfix/Chat/ChatService.cs").Content;
        Assert.Contains("[HotfixService(typeof(IChatService))]", chatService, StringComparison.Ordinal);
        Assert.Contains("internal sealed class ChatService", chatService, StringComparison.Ordinal);
        Assert.Contains("public static async ValueTask BindAsync(HotfixServiceCall<ChatBindRequest, IChatCallback> call)", chatService, StringComparison.Ordinal);
        Assert.Contains("public static async ValueTask SendAsync(HotfixServiceCall<ChatSendRequest, IChatCallback> call)", chatService, StringComparison.Ordinal);
        Assert.Contains("AskAsync<ChatRoomActor", chatService, StringComparison.Ordinal);
        Assert.Contains("badword", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatServiceCall", chatService, StringComparison.Ordinal);

        var behavior = Assert.Single(plan.Files, file => file.RelativePath == "Server/Hotfix/Chat/ChatRoomBehavior.cs").Content;
        Assert.Contains("[HotfixBehaviorOf(typeof(ChatRoomActor))]", behavior, StringComparison.Ordinal);
        Assert.Contains("public static ValueTask<LoginReply> LoginAsync", behavior, StringComparison.Ordinal);
        Assert.Contains("public static ValueTask LeaveAsync", behavior, StringComparison.Ordinal);

        Assert.DoesNotContain(plan.Files, file => file.Content.Contains("static event", StringComparison.OrdinalIgnoreCase));
    }
}
