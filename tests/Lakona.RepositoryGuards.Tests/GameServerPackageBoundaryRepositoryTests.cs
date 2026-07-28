using System.Xml.Linq;
using Lakona.RepositoryGuards.Tests.PackageVersions;
using Xunit;

namespace Lakona.RepositoryGuards.Tests;

public sealed class GameServerPackageBoundaryRepositoryTests
{
    [Theory]
    [InlineData(@"..\Lakona.Rpc.Transport.Tcp\Lakona.Rpc.Transport.Tcp.csproj")]
    [InlineData("../Lakona.Rpc.Transport.Tcp/Lakona.Rpc.Transport.Tcp.csproj")]
    public void Project_reference_names_are_independent_of_path_separator(string include)
    {
        var project = XDocument.Parse(
            $"""
             <Project>
               <ItemGroup>
                 <ProjectReference Include="{include}" />
               </ItemGroup>
             </Project>
             """);

        Assert.Equal(
            ["Lakona.Rpc.Transport.Tcp"],
            ReadProjectReferenceNames(project));
    }

    [Fact]
    public void Game_server_package_owns_fixed_cluster_rpc_without_optional_endpoint_implementations()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "src",
            "Lakona.Game.Server",
            "Lakona.Game.Server.csproj");
        var references = ReadProjectReferenceNames(XDocument.Load(projectPath));
        var standaloneClusterProject = Path.Combine(
            repositoryRoot,
            "src",
            "Lakona.Game.Cluster",
            "Lakona.Game.Cluster.csproj");

        Assert.False(File.Exists(standaloneClusterProject));
        Assert.DoesNotContain("Lakona.Game.Cluster", references);
        Assert.Contains("Lakona.Rpc.Transport.Tcp", references);
        Assert.DoesNotContain("Lakona.Rpc.Transport.Kcp", references);
        Assert.DoesNotContain("Lakona.Rpc.Transport.WebSocket", references);
        Assert.DoesNotContain("Lakona.Rpc.Serializer.Json", references);
        Assert.Contains("Lakona.Rpc.Serializer.MemoryPack", references);
        Assert.DoesNotContain("Lakona.Game.Cluster.Rpc.Transport.Tcp", references);
        Assert.DoesNotContain("Lakona.Game.Cluster.Rpc.Serializer.Json", references);
        Assert.DoesNotContain("Lakona.Game.Cluster.Rpc.Serializer.MemoryPack", references);
    }

    private static HashSet<string> ReadProjectReferenceNames(XDocument project) =>
        project
            .Descendants("ProjectReference")
            .Select(static reference => (string?)reference.Attribute("Include"))
            .Where(static include => !string.IsNullOrWhiteSpace(include))
            .Select(static include =>
                Path.GetFileNameWithoutExtension(include!.Replace('\\', '/')))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
