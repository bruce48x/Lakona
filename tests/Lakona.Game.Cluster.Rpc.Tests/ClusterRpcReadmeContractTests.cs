using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ClusterRpcDocumentationContractTests
{
    [Fact]
    public void Cluster_documentation_does_not_restore_removed_service_bootstrap_model()
    {
        var readme = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "cluster.md"));

        Assert.DoesNotContain("Lakona:Cluster:Services", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("ClusterService", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("NodeServiceDescriptor", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Services\"", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("Cluster:Bootstrap:NodeDirectoryEndpoints", readme, StringComparison.Ordinal);
        Assert.Contains("\"Seeds\"", readme, StringComparison.Ordinal);
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
