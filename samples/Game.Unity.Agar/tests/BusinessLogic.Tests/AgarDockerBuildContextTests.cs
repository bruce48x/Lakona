using Xunit;

namespace Agar.Unity.Tests;

public sealed class AgarDockerBuildContextTests
{
    [Fact]
    public void ServerDockerfileCopiesSourceTreeAsSingleBuildContext()
    {
        var root = FindRepositoryRoot();
        var dockerfile = File.ReadAllText(Path.Combine(root, "samples", "Game.Unity.Agar", "Server", "Dockerfile"));
        var dockerignore = File.ReadAllText(Path.Combine(root, "samples", "Game.Unity.Agar", "Server", "Dockerfile.dockerignore"));

        Assert.Contains("COPY src/ src/", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("COPY src/Lakona.", dockerfile, StringComparison.Ordinal);
        Assert.Contains("!src/", dockerignore, StringComparison.Ordinal);
        Assert.Contains("!src/**", dockerignore, StringComparison.Ordinal);
        Assert.DoesNotContain("!src/Lakona.", dockerignore, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository root from '{AppContext.BaseDirectory}'.");
    }
}
