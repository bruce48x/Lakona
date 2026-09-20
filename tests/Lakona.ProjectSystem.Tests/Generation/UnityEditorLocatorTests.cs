using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Execution;
using Xunit;

namespace Lakona.ProjectSystem.Tests.Generation;

public sealed class UnityEditorLocatorTests
{
    [Fact]
    public void SelectInstallation_rejects_a_probed_incompatible_editor_despite_a_compatible_directory_name()
    {
        // The executable really reports 6000.4 while its stale directory is still
        // named 6000.3.13f1. Accepting it would restore with an incompatible
        // editor and record a version that never executed.
        var probed = new UnityEditorInstallation(
            "/opt/unity/6000.4.13f1/Editor/Unity",
            "6000.4.13f1",
            "abc123def456");

        var selected = UnityEditorLocator.SelectInstallation(
            probed,
            "/opt/unity/Hub/Editor/6000.3.13f1/Editor/Unity",
            "6000.3.3f1");

        Assert.Null(selected);
    }

    [Theory]
    [InlineData("6000.3.13f1", "6000.3.3f1")]
    [InlineData("2022.3.62f3c1", "2022.3.72f1")]
    public void SelectInstallation_keeps_the_probed_version_and_revision_when_compatible(
        string probedVersion,
        string expected)
    {
        // The directory name deliberately describes another stream so the probed
        // result is the only value that can satisfy the expectation.
        var selected = UnityEditorLocator.SelectInstallation(
            new UnityEditorInstallation("/opt/unity/Editor/Unity", probedVersion, "abc123def456"),
            "/opt/unity/Hub/Editor/6000.9.99f1/Editor/Unity",
            expected);

        Assert.NotNull(selected);
        Assert.Equal(probedVersion, selected.Version);
        Assert.Equal("abc123def456", selected.Revision);
    }

    [Theory]
    [InlineData("/opt/unity/Hub/Editor/6000.3.13f1/Editor/Unity", "6000.3.13f1")]
    [InlineData("/opt/unity/Hub/Editor/2022.3.62f3c1/Editor/Unity", "2022.3.62f3c1")]
    public void SelectInstallation_falls_back_to_the_directory_name_only_when_probing_is_indeterminate(
        string candidatePath,
        string expectedFromPath)
    {
        var selected = UnityEditorLocator.SelectInstallation(null, candidatePath, expectedFromPath);

        Assert.NotNull(selected);
        Assert.Equal(expectedFromPath, selected.Version);
        Assert.Null(selected.Revision);
    }

    [Theory]
    [InlineData("/opt/unity/Hub/Editor/6000.4.13f1/Editor/Unity", "6000.3.3f1")]
    [InlineData("/opt/unity/Hub/Editor/Unity", "6000.3.3f1")]
    public void SelectInstallation_rejects_when_probing_is_indeterminate_and_the_path_is_not_compatible(
        string candidatePath,
        string expected)
    {
        Assert.Null(UnityEditorLocator.SelectInstallation(null, candidatePath, expected));
    }
}
