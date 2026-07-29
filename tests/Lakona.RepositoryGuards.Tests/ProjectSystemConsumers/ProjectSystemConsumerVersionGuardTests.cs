using Lakona.RepositoryGuards.Tests.PackageVersions;
using Xunit;

namespace Lakona.RepositoryGuards.Tests.ProjectSystemConsumers;

public sealed class ProjectSystemConsumerVersionGuardTests
{
    [Fact]
    public void Consumer_version_must_change_when_generated_package_version_changes()
    {
        var inputs = ProjectSystemReleaseInputs.Create(
            "src/Lakona.Game.Server/Lakona.Game.Server.csproj");

        var result = ProjectSystemConsumerVersionGuard.Evaluate(
            "Lakona Hub",
            "C:/repo",
            "1.0.0",
            "1.0.0",
            ["src/Lakona.Game.Server/Lakona.Game.Server.csproj"],
            inputs);

        Assert.False(result.Succeeded);
        Assert.Equal("Lakona Hub", result.ConsumerName);
    }

    [Fact]
    public void Release_inputs_come_from_ProjectSystem_and_its_generated_package_versions()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();

        var inputs = ProjectSystemReleaseInputs.ReadCurrent(repositoryRoot);

        Assert.True(inputs.Contains("src/Lakona.ProjectSystem/Generation/Planning/GenerationPlan.cs"));
        Assert.True(inputs.Contains("src/Lakona.Game.Server/Lakona.Game.Server.csproj"));
        Assert.True(inputs.Contains("skills/lakona-implement-service/SKILL.md"));
        Assert.False(inputs.Contains("src/Lakona.Tool/Lakona.Tool.csproj"));
    }

    [Fact]
    public void ProjectSystem_documentation_is_not_a_release_input()
    {
        var inputs = ProjectSystemReleaseInputs.Create();

        Assert.False(inputs.Contains("src/Lakona.ProjectSystem/README.md"));
    }

    [Fact]
    public void Repository_build_configuration_is_a_shared_release_input()
    {
        var inputs = ProjectSystemReleaseInputs.Create();

        Assert.True(inputs.Contains("Directory.Build.props"));
    }
}
