using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Operations;
using Xunit;

namespace Lakona.Tool.Tests.Rendering;

public sealed class OperationsRendererTests
{
    [Fact]
    public void AddFiles_NoneProfile_EmitsNoFiles()
    {
        var plan = Render(Spec(DeploymentProfile.None));

        Assert.Empty(plan.Files);
    }

    [Fact]
    public void AddFiles_ComposeProfile_EmitsComposeFilesUsingPublishedServerApp()
    {
        var plan = Render(Spec(DeploymentProfile.Compose));

        var dockerfile = Assert.Single(plan.Files, file => file.RelativePath == "Server/Dockerfile").Content;
        var compose = Assert.Single(plan.Files, file => file.RelativePath == "docker-compose.cluster.yml").Content;
        Assert.Contains("RUN dotnet publish Server/App/Server.App.csproj -c Release -o /app", dockerfile, StringComparison.Ordinal);
        Assert.Contains("ENTRYPOINT [\"dotnet\", \"Server.App.dll\"]", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("command:", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet run", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("Server/Server", compose, StringComparison.Ordinal);
        Assert.Contains(plan.Files, file => file.RelativePath == ".env.cluster.example");
        Assert.Contains(plan.Files, file => file.RelativePath == "ops/CLUSTER_OPERATIONS.md");
    }

    private static GenerationPlan Render(LakonaProjectSpec spec)
    {
        var builder = new GenerationPlanBuilder("Root");
        new OperationsRenderer().AddFiles(spec, builder);
        return builder.Build();
    }

    private static LakonaProjectSpec Spec(DeploymentProfile deploymentProfile)
    {
        return new LakonaProjectSpecFactory().Create(new NewProjectOptions(
            "MyGame",
            ".",
            ClientEngine.Unity,
            TransportKind.Kcp,
            SerializerKind.MemoryPack,
            PersistenceKind.None,
            NuGetForUnitySource.OpenUpm,
            deploymentProfile));
    }
}
