using Lakona.RepositoryGuards.Tests.ProjectSystemConsumers;
using Xunit;

namespace Lakona.RepositoryGuards.Tests.HubVersions;

public sealed class HubVersionGuardTests
{
    [Theory]
    [InlineData("src/Lakona.Hub/MainWindow.axaml")]
    [InlineData("scripts/hub/New-HubRelease.ps1")]
    [InlineData(".github/workflows/publish-hub.yml")]
    [InlineData(".github/workflows/tests-linux.yml")]
    public void HubVersionGuard_RequiresVersionBumpForReleaseInputs(string changedPath)
    {
        var result = HubVersionGuard.Evaluate("C:/repo", "1.0.0", "1.0.0", [changedPath]);

        Assert.False(result.Succeeded);
        Assert.Contains(changedPath, result.ChangedInputs);
    }

    [Fact]
    public void HubVersionGuard_AcceptsReleaseInputWhenVersionChanges()
    {
        var result = HubVersionGuard.Evaluate(
            "C:/repo",
            "1.0.0",
            "1.0.1",
            ["src/Lakona.Hub/MainWindow.axaml"]);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void HubVersionGuard_DoesNotApplyProjectSystemConsumerPolicy()
    {
        var result = HubVersionGuard.Evaluate(
            "C:/repo",
            "1.0.0",
            "1.0.0",
            ["src/Lakona.Game.Server/Lakona.Game.Server.csproj"]);

        Assert.True(result.Succeeded);
        Assert.Empty(result.ChangedInputs);
    }

    [Fact]
    public void Hub_change_scope_includes_shared_ProjectSystem_inputs()
    {
        var inputs = ProjectSystemReleaseInputs.Create(
            "src/Lakona.Game.Server/Lakona.Game.Server.csproj");

        var scope = HubVersionGuard.CreateScope(inputs);

        Assert.True(scope.IsRelevantPath("src/Lakona.Game.Server/Lakona.Game.Server.csproj"));
    }

    [Fact]
    public void HubVersionGuard_IgnoresUnrelatedChanges()
    {
        var result = HubVersionGuard.Evaluate("C:/repo", "1.0.0", "1.0.0", ["docs/rpc/architecture.md"]);

        Assert.True(result.Succeeded);
        Assert.Empty(result.ChangedInputs);
    }
}
