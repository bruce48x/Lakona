using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Execution;
using Xunit;

namespace Lakona.ProjectSystem.Tests.Generation;

public sealed class UnityEditorVersionTests
{
    [Fact]
    public void VersionedEditorCandidates_EnumeratesCompatibleMacEditorInstallations()
    {
        var root = Path.Combine(Path.GetTempPath(), nameof(UnityEditorVersionTests), Guid.NewGuid().ToString("N"));
        var compatibleExecutable = Path.Combine(
            root,
            "6000.3.13f1",
            "Unity.app",
            "Contents",
            "MacOS",
            "Unity");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(compatibleExecutable)!);
            File.WriteAllText(compatibleExecutable, string.Empty);

            var candidates = UnityEditorLocator.VersionedEditorCandidates(
                root,
                "6000.3.3f1",
                "Unity.app/Contents/MacOS/Unity");

            Assert.Contains(compatibleExecutable, candidates);
            Assert.Contains(
                Path.Combine(root, "6000.3.3f1", "Unity.app", "Contents", "MacOS", "Unity"),
                candidates);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("6000.3.13f1", "6000.3.3f1")]
    [InlineData("2022.3.72f1", "2022.3.62f3c1")]
    [InlineData("2022.3.61t8", "2022.3.61t8")]
    public void IsCompatible_MatchesMajorAndMinorOnly(string actual, string expected)
    {
        Assert.True(UnityEditorVersion.IsCompatible(actual, expected));
    }

    [Theory]
    [InlineData("6000.4.13f1", "6000.3.3f1")]
    [InlineData("2021.3.72f1", "2022.3.62f3c1")]
    [InlineData("not-a-version", "2022.3.62f3c1")]
    public void IsCompatible_RejectsDifferentStreams(string actual, string expected)
    {
        Assert.False(UnityEditorVersion.IsCompatible(actual, expected));
    }
}
