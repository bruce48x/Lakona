using Lakona.RepositoryGuards.Tests.ProjectSystemConsumers;
using Xunit;

namespace Lakona.RepositoryGuards.Tests.PackageVersions;

public sealed class PackageVersionGraphRepositoryTests
{
    [Fact]
    public void Package_versions_are_bumped_for_changed_dependency_graph()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
        var changeSet = GitChangeSetReader.Read(repositoryRoot);
        var baseProjects = PackageProjectReader.ReadAtGitRef(repositoryRoot, changeSet.BaseRef);
        var headProjects = PackageProjectReader.ReadAtGitRef(repositoryRoot, changeSet.HeadRef);

        var packageResult = PackageVersionGuard.Evaluate(baseProjects, headProjects, changeSet.ChangedPaths);
        var inputs = ProjectSystemReleaseInputs.ReadCurrent(repositoryRoot);
        var consumerResult = ProjectSystemConsumerVersionGuard.Evaluate(
            "Lakona.Tool",
            repositoryRoot,
            ReadVersion(baseProjects, "Lakona.Tool"),
            ReadVersion(headProjects, "Lakona.Tool"),
            changeSet.ChangedPaths,
            inputs);

        Assert.True(
            packageResult.Failures.Count == 0 && consumerResult.Succeeded,
            FormatFailures(changeSet, packageResult, consumerResult));
    }

    private static string ReadVersion(IReadOnlyList<PackageProject> projects, string packageId)
    {
        return projects.Single(project => string.Equals(project.PackageId, packageId, StringComparison.Ordinal)).Version;
    }

    private static string FormatFailures(
        GitChangeSet changeSet,
        PackageVersionGuardResult packageResult,
        ProjectSystemConsumerVersionGuardResult consumerResult)
    {
        return "Package version graph guard failed." + Environment.NewLine +
               $"Base: {changeSet.BaseRef}" + Environment.NewLine +
               $"Head: {changeSet.HeadRef}" + Environment.NewLine +
               string.Join(Environment.NewLine, packageResult.Failures.Select(failure =>
                   $"- {failure.PackageId} {failure.CurrentVersion}: {failure.Reason}")) +
               (consumerResult.Succeeded
                   ? string.Empty
                   : Environment.NewLine +
                     $"- {consumerResult.ConsumerName} {consumerResult.HeadVersion}: version must change because ProjectSystem release inputs changed.");
    }
}
