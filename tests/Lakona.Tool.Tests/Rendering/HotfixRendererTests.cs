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

        var loginService = Assert.Single(plan.Files, file => file.RelativePath == "Server/Hotfix/Login/LoginService.cs").Content;
        Assert.Contains("internal sealed class LoginService : ILoginService", loginService, StringComparison.Ordinal);
        Assert.Contains("private readonly ILoginCallback _callback;", loginService, StringComparison.Ordinal);
        Assert.Contains("private readonly IActorRuntime _actors;", loginService, StringComparison.Ordinal);
        Assert.Contains("return _actors.AskAsync<ChatRoomActor, LoginReply>", loginService, StringComparison.Ordinal);

        var chatService = Assert.Single(plan.Files, file => file.RelativePath == "Server/Hotfix/Chat/ChatService.cs").Content;
        Assert.Contains("internal sealed class ChatService : IChatService", chatService, StringComparison.Ordinal);
        Assert.Contains("private readonly IChatCallback _callback;", chatService, StringComparison.Ordinal);
        Assert.Contains("private readonly IActorRuntime _actors;", chatService, StringComparison.Ordinal);
        Assert.Contains("public async ValueTask BindAsync(ChatBindRequest req)", chatService, StringComparison.Ordinal);
        Assert.Contains("public async ValueTask SendAsync(ChatSendRequest req)", chatService, StringComparison.Ordinal);
        Assert.Contains("AskAsync<ChatRoomActor", chatService, StringComparison.Ordinal);
        Assert.Contains("badword", chatService, StringComparison.Ordinal);

        Assert.DoesNotContain(plan.Files, file => file.Content.Contains("static event", StringComparison.OrdinalIgnoreCase));
    }
}
