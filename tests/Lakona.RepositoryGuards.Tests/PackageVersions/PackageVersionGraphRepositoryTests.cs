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

        var result = PackageVersionGuard.Evaluate(baseProjects, headProjects, changeSet.ChangedPaths);

        Assert.True(result.Failures.Count == 0, FormatFailures(changeSet, result));
    }

    private static string FormatFailures(GitChangeSet changeSet, PackageVersionGuardResult result)
    {
        return "Package version graph guard failed." + Environment.NewLine +
               $"Base: {changeSet.BaseRef}" + Environment.NewLine +
               $"Head: {changeSet.HeadRef}" + Environment.NewLine +
               string.Join(Environment.NewLine, result.Failures.Select(failure =>
                   $"- {failure.PackageId} {failure.CurrentVersion}: {failure.Reason}"));
    }
}
