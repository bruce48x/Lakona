using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Planning;
using Lakona.ProjectSystem.Generation.Rendering.Common;
using Lakona.ProjectSystem.Generation.Rendering.Client;
using Lakona.ProjectSystem.Generation.Rendering.Shared;
using Xunit;

namespace Lakona.ProjectSystem.Tests.Planning;

public sealed class LakonaProjectPlanBuilderTests
{
    [Fact]
    public void Build_IncludesContributorFilesAndValidatesPlan()
    {
        var spec = new ProjectSpecTestFactory().Create(new ProjectSpecTestOptions(
            "MyGame",
            ".",
            ClientEngine.Unity,
            TransportKind.Kcp,
            SerializerKind.MemoryPack,
            NuGetForUnitySource.OpenUpm,
            DeploymentProfile.None));
        var planBuilder = new LakonaProjectPlanBuilder(
            [
                new GitRenderer(),
                new SharedProjectRenderer()
            ]);

        var plan = planBuilder.Build(spec);

        Assert.Equal(spec.Layout.RootPath, plan.RootPath);
        Assert.Contains(plan.Files, file => file.RelativePath == ".gitignore");
        Assert.DoesNotContain(plan.Files, file => file.RelativePath == "lakona-game.tool.json");
        Assert.Contains(plan.Files, file => file.RelativePath == "Shared/Shared.csproj");
        Assert.DoesNotContain(plan.Diagnostics, diagnostic => diagnostic.Severity == PlanDiagnosticSeverity.Error);
    }

    [Fact]
    public void Build_ReportsDuplicateContributorPaths()
    {
        var spec = new ProjectSpecTestFactory().Create(new ProjectSpecTestOptions(
            "MyGame",
            ".",
            ClientEngine.Unity,
            TransportKind.Kcp,
            SerializerKind.MemoryPack,
            NuGetForUnitySource.OpenUpm,
            DeploymentProfile.None));
        var planBuilder = new LakonaProjectPlanBuilder([new DuplicateContributor()]);

        var plan = planBuilder.Build(spec);

        Assert.Contains(plan.Diagnostics, diagnostic => diagnostic.Code == "LTPLAN001");
    }

    [Fact]
    public void Build_SelectsOnlyMatchingClientRenderer()
    {
        var spec = new ProjectSpecTestFactory().Create(new ProjectSpecTestOptions(
            "MyGame",
            ".",
            ClientEngine.Godot,
            TransportKind.Kcp,
            SerializerKind.MemoryPack,
            NuGetForUnitySource.OpenUpm,
            DeploymentProfile.None));
        var planBuilder = new LakonaProjectPlanBuilder([], [new UnityClientRenderer(), new GodotClientRenderer(), new ConsoleClientRenderer()]);

        var plan = planBuilder.Build(spec);

        Assert.Contains(plan.Files, file => file.RelativePath == "Client/project.godot");
        Assert.DoesNotContain(plan.Files, file => file.RelativePath.StartsWith("Client/Assets/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Unity")]
    [InlineData("Tuanjie")]
    public void Build_SelectsUnityRenderer_ForUnityCompatibleEngines(string engineName)
    {
        var engine = Enum.Parse<ClientEngine>(engineName);
        var spec = new ProjectSpecTestFactory().Create(new ProjectSpecTestOptions(
            "MyGame",
            ".",
            engine,
            TransportKind.Kcp,
            SerializerKind.MemoryPack,
            NuGetForUnitySource.OpenUpm,
            DeploymentProfile.None));
        var planBuilder = new LakonaProjectPlanBuilder([], [new UnityClientRenderer(), new GodotClientRenderer(), new ConsoleClientRenderer()]);

        var plan = planBuilder.Build(spec);

        Assert.Contains(plan.Files, file => file.RelativePath == "Client/Packages/manifest.json");
        Assert.Contains(plan.Files, file => file.RelativePath == "Client/Assets/packages.config");
        Assert.DoesNotContain(plan.Files, file => file.RelativePath == "Client/project.godot");
    }

    private sealed class DuplicateContributor : IPlanContributor
    {
        public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
        {
            builder.AddFile("same.txt", "a", FileWriteMode.Replace, GeneratedFileKind.Text);
            builder.AddFile("same.txt", "b", FileWriteMode.Replace, GeneratedFileKind.Text);
        }
    }
}
