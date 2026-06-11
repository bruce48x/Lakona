using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Docs;
using Xunit;

namespace Lakona.Tool.Tests.Rendering;

public sealed class GeneratedProjectDocsRendererTests
{
    [Fact]
    public void AddFiles_EmitsGeneratedProjectDocs()
    {
        var spec = new LakonaProjectSpecFactory().Create(new NewProjectOptions(
            "MyGame",
            ".",
            ClientEngine.Godot,
            TransportKind.WebSocket,
            SerializerKind.Json,
            PersistenceKind.None,
            NuGetForUnitySource.OpenUpm,
            DeploymentProfile.None));
        var builder = new GenerationPlanBuilder("Root");

        new GeneratedProjectDocsRenderer().AddFiles(spec, builder);

        var plan = builder.Build();
        Assert.Contains(plan.Files, file => file.RelativePath == "docs/GETTING_STARTED.md");
        Assert.Contains(plan.Files, file => file.RelativePath == "docs/EDITING_GUIDE.md");
        Assert.Contains(plan.Files, file => file.RelativePath == "docs/OPERATIONS.md");
        Assert.Contains("Shared/Contracts/", Assert.Single(plan.Files, file => file.RelativePath == "docs/EDITING_GUIDE.md").Content, StringComparison.Ordinal);
        Assert.Contains("Server/Hotfix/", Assert.Single(plan.Files, file => file.RelativePath == "docs/EDITING_GUIDE.md").Content, StringComparison.Ordinal);
    }
}
