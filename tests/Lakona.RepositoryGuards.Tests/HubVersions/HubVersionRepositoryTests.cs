using Lakona.RepositoryGuards.Tests.PackageVersions;
using Lakona.RepositoryGuards.Tests.ProjectSystemConsumers;
using Xunit;

namespace Lakona.RepositoryGuards.Tests.HubVersions;

public sealed class HubVersionRepositoryTests
{
    [Fact]
    public void HubVersion_is_bumped_for_changed_release_inputs()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
        var inputs = ProjectSystemReleaseInputs.ReadCurrent(repositoryRoot);
        var changeSet = GitChangeSetReader.Read(repositoryRoot, HubVersionGuard.CreateScope(inputs));
        var baseVersion = HubVersionProjectReader.Read(repositoryRoot, changeSet.BaseRef);
        var headVersion = HubVersionProjectReader.Read(repositoryRoot, changeSet.HeadRef);

        var hubResult = HubVersionGuard.Evaluate(
            repositoryRoot,
            baseVersion,
            headVersion,
            changeSet.ChangedPaths);
        var consumerResult = ProjectSystemConsumerVersionGuard.Evaluate(
            "Lakona Hub",
            repositoryRoot,
            baseVersion,
            headVersion,
            changeSet.ChangedPaths,
            inputs);

        Assert.True(
            hubResult.Succeeded && consumerResult.Succeeded,
            FormatFailure(changeSet, hubResult, consumerResult));
    }

    private static string FormatFailure(
        GitChangeSet changeSet,
        HubVersionGuardResult hubResult,
        ProjectSystemConsumerVersionGuardResult consumerResult)
    {
        return "Hub version guard failed." + Environment.NewLine +
               $"Base: {changeSet.BaseRef} ({hubResult.BaseVersion})" + Environment.NewLine +
               $"Head: {changeSet.HeadRef} ({hubResult.HeadVersion})" + Environment.NewLine +
               "Bump src/Lakona.Hub/Lakona.Hub.csproj because these Hub release inputs changed:" + Environment.NewLine +
               string.Join(
                   Environment.NewLine,
                   hubResult.ChangedInputs
                       .Concat(consumerResult.ChangedInputs)
                       .Distinct(StringComparer.Ordinal)
                       .OrderBy(path => path, StringComparer.Ordinal)
                       .Select(path => $"- {path}"));
    }
}
