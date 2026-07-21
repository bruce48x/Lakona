using System.Xml.Linq;
using Lakona.RepositoryGuards.Tests.PackageVersions;
using Xunit;

namespace Lakona.RepositoryGuards.Tests.HubVersions;

public sealed class HubVersionGuardTests
{
    [Theory]
    [InlineData("src/Lakona.Hub/MainWindow.axaml")]
    [InlineData("src/Lakona.ProjectSystem/Generation/Planning/GenerationPlan.cs")]
    [InlineData("src/Lakona.Game.Server/Lakona.Game.Server.csproj")]
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
    public void HubVersionGuard_TracksEveryGeneratedPackageVersionInput()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
        var projectPath = Path.Combine(repositoryRoot, "src", "Lakona.ProjectSystem", "Lakona.ProjectSystem.csproj");
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var packageVersionInputs = XDocument.Load(projectPath)
            .Descendants("XmlPeek")
            .Select(element => element.Attribute("XmlInputPath")?.Value)
            .OfType<string>()
            .Select(path => path.Replace("$(MSBuildProjectDirectory)", projectDirectory, StringComparison.Ordinal))
            .Select(path => Path.GetFullPath(path.Replace('\\', Path.DirectorySeparatorChar)))
            .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))
            .ToArray();

        Assert.NotEmpty(packageVersionInputs);
        Assert.All(
            packageVersionInputs,
            path => Assert.True(
                HubVersionGuard.IsReleaseInputPath(path),
                $"Hub version guard does not track generated package-version input '{path}'."));
    }

    [Fact]
    public void HubVersionGuard_IgnoresUnrelatedChanges()
    {
        var result = HubVersionGuard.Evaluate("C:/repo", "1.0.0", "1.0.0", ["docs/rpc.md"]);

        Assert.True(result.Succeeded);
        Assert.Empty(result.ChangedInputs);
    }
}
