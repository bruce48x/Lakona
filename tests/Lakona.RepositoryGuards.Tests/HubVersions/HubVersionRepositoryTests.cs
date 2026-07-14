using Lakona.RepositoryGuards.Tests.PackageVersions;
using Xunit;

namespace Lakona.RepositoryGuards.Tests.HubVersions;

public sealed class HubVersionRepositoryTests
{
    [Fact]
    public void HubVersion_is_bumped_for_changed_release_inputs()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
        var changeSet = GitChangeSetReader.Read(repositoryRoot, HubVersionGuard.Scope);
        var baseVersion = HubVersionProjectReader.Read(repositoryRoot, changeSet.BaseRef);
        var headVersion = HubVersionProjectReader.Read(repositoryRoot, changeSet.HeadRef);

        var result = HubVersionGuard.Evaluate(
            repositoryRoot,
            baseVersion,
            headVersion,
            changeSet.ChangedPaths);

        Assert.True(result.Succeeded, FormatFailure(changeSet, result));
    }

    private static string FormatFailure(GitChangeSet changeSet, HubVersionGuardResult result)
    {
        return "Hub version guard failed." + Environment.NewLine +
               $"Base: {changeSet.BaseRef} ({result.BaseVersion})" + Environment.NewLine +
               $"Head: {changeSet.HeadRef} ({result.HeadVersion})" + Environment.NewLine +
               "Bump src/Lakona.Hub/Lakona.Hub.csproj because these Hub release inputs changed:" + Environment.NewLine +
               string.Join(Environment.NewLine, result.ChangedInputs.Select(path => $"- {path}"));
    }
}
