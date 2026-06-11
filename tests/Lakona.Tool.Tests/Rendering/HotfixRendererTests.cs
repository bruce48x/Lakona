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
        Assert.Contains(plan.Files, file => file.RelativePath == "Server/Hotfix/Login/LoginService.cs");
        Assert.Contains(plan.Files, file => file.RelativePath == "Server/Hotfix/Chat/ChatService.cs");
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains("static event", StringComparison.OrdinalIgnoreCase));
    }
}
