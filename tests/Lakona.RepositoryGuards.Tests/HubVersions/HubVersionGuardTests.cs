using Xunit;

namespace Lakona.RepositoryGuards.Tests.HubVersions;

public sealed class HubVersionGuardTests
{
    [Theory]
    [InlineData("src/Lakona.Hub/MainWindow.axaml")]
    [InlineData("src/Lakona.ProjectSystem/Generation/Planning/GenerationPlan.cs")]
    [InlineData("scripts/hub/New-HubRelease.ps1")]
    [InlineData(".github/workflows/publish-hub.yml")]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    [InlineData("global.json")]
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
            ["src/Lakona.ProjectSystem/Generation/Planning/GenerationPlan.cs"]);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void HubVersionGuard_IgnoresUnrelatedChanges()
    {
        var result = HubVersionGuard.Evaluate("C:/repo", "1.0.0", "1.0.0", ["docs/rpc.md"]);

        Assert.True(result.Succeeded);
        Assert.Empty(result.ChangedInputs);
    }
}
