using Xunit;

namespace Lakona.RepositoryGuards.Tests;

public sealed class UnityPackageTrackingRepositoryTests
{
    [Fact]
    public void RepositoryDoesNotIgnoreRestoredUnityPackages()
    {
        var root = FindRepositoryRoot();
        var gitIgnore = File.ReadAllText(Path.Combine(root, ".gitignore"));

        Assert.DoesNotContain("Assets/Packages", gitIgnore, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not find the Lakona repository root.");
    }
}
