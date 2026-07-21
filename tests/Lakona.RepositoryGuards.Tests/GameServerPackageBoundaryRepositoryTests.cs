using System.Xml.Linq;
using Lakona.RepositoryGuards.Tests.PackageVersions;
using Xunit;

namespace Lakona.RepositoryGuards.Tests;

public sealed class GameServerPackageBoundaryRepositoryTests
{
    [Fact]
    public void Game_server_package_does_not_transitively_install_optional_endpoint_implementations()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "src",
            "Lakona.Game.Server",
            "Lakona.Game.Server.csproj");
        var references = XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension((string?)reference.Attribute("Include")))
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("Lakona.Rpc.Transport.Tcp", references);
        Assert.DoesNotContain("Lakona.Rpc.Transport.Kcp", references);
        Assert.DoesNotContain("Lakona.Rpc.Transport.WebSocket", references);
        Assert.DoesNotContain("Lakona.Rpc.Serializer.Json", references);
        Assert.DoesNotContain("Lakona.Rpc.Serializer.MemoryPack", references);
        Assert.DoesNotContain("Lakona.Game.Cluster.Rpc.Transport.Tcp", references);
        Assert.DoesNotContain("Lakona.Game.Cluster.Rpc.Serializer.Json", references);
        Assert.DoesNotContain("Lakona.Game.Cluster.Rpc.Serializer.MemoryPack", references);
    }
}
