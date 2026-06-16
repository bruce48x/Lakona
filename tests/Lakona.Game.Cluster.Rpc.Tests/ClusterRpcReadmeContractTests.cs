using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ClusterRpcReadmeContractTests
{
    [Fact]
    public void ReadmeDoesNotDocumentRemovedClusterServiceBootstrapModel()
    {
        var readme = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Lakona.Game.Cluster.Rpc", "README.md"));

        Assert.DoesNotContain("Lakona:Cluster:Services", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("ClusterService", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("NodeServiceDescriptor", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Services\"", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("Cluster:Bootstrap:NodeDirectoryEndpoints", readme, StringComparison.Ordinal);
        Assert.Contains("Lakona:Cluster:Seeds", readme, StringComparison.Ordinal);
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
