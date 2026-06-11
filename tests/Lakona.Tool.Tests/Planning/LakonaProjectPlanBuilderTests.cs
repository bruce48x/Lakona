using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Common;
using Lakona.Tool.Rendering.Project;
using Lakona.Tool.Rendering.Client;
using Lakona.Tool.Rendering.Shared;
using Xunit;

namespace Lakona.Tool.Tests.Planning;

public sealed class LakonaProjectPlanBuilderTests
{
    [Fact]
    public void Build_IncludesContributorFilesAndValidatesPlan()
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
        var planBuilder = new LakonaProjectPlanBuilder(
            [
                new GitRenderer(),
                new ProjectConfigRenderer(),
                new SharedProjectRenderer()
            ]);

        var plan = planBuilder.Build(spec);

        Assert.Equal(spec.Layout.RootPath, plan.RootPath);
        Assert.Contains(plan.Files, file => file.RelativePath == ".gitignore");
        Assert.Contains(plan.Files, file => file.RelativePath == "lakona-game.tool.json");
        Assert.Contains(plan.Files, file => file.RelativePath == "Shared/Shared.csproj");
        Assert.DoesNotContain(plan.Diagnostics, diagnostic => diagnostic.Severity == PlanDiagnosticSeverity.Error);
    }

    [Fact]
    public void Build_ReportsDuplicateContributorPaths()
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
        var planBuilder = new LakonaProjectPlanBuilder([new DuplicateContributor()]);

        var plan = planBuilder.Build(spec);

        Assert.Contains(plan.Diagnostics, diagnostic => diagnostic.Code == "LTPLAN001");
    }

    [Fact]
    public void Build_SelectsOnlyMatchingClientRenderer()
    {
        var spec = new LakonaProjectSpecFactory().Create(new NewProjectOptions(
            "MyGame",
            ".",
            ClientEngine.Godot,
            TransportKind.Kcp,
            SerializerKind.MemoryPack,
            PersistenceKind.None,
            NuGetForUnitySource.OpenUpm,
            DeploymentProfile.None));
        var planBuilder = new LakonaProjectPlanBuilder([], [new UnityClientRenderer(), new GodotClientRenderer()]);

        var plan = planBuilder.Build(spec);

        Assert.Contains(plan.Files, file => file.RelativePath == "Client/project.godot");
        Assert.DoesNotContain(plan.Files, file => file.RelativePath.StartsWith("Client/Assets/", StringComparison.Ordinal));
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
