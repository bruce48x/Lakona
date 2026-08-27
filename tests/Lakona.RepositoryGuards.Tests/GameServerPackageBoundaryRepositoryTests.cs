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

    [Fact]
    public void Membership_storage_clients_are_owned_by_adapter_packages()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
        var server = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "Lakona.Game.Server",
            "Lakona.Game.Server.csproj"));
        var postgres = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "Lakona.Game.Clustering.Postgres",
            "Lakona.Game.Clustering.Postgres.csproj"));

        var serverPackages = ReadPackageReferenceNames(server);
        Assert.DoesNotContain("Npgsql", serverPackages);
        Assert.DoesNotContain("MySqlConnector", serverPackages);
        Assert.DoesNotContain("StackExchange.Redis", serverPackages);

        Assert.Contains("Lakona.Game.Server", ReadProjectReferenceNames(postgres));
        Assert.Contains("Npgsql", ReadPackageReferenceNames(postgres));
    }

    [Fact]
    public void Distributed_actor_directory_implementation_is_cluster_owned()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
        var actorRuntime = Path.Combine(
            repositoryRoot,
            "src",
            "Lakona.Game.Server",
            "Actors");
        var clusterActors = Path.Combine(
            repositoryRoot,
            "src",
            "Lakona.Game.Server",
            "Cluster",
            "Actors");

        Assert.False(
            Directory.EnumerateFiles(
                actorRuntime,
                "*ActorLocation*.cs",
                SearchOption.AllDirectories).Any(),
            "Distributed Actor Directory implementation must not return to the process-local Actors module.");
        Assert.All(
            new[]
            {
                "IActorActivationDirectory.cs",
                "ActorDirectoryRegisterStatus.cs",
                "ActorDirectoryUnregisterStatus.cs"
            },
            file => Assert.False(
                File.Exists(Path.Combine(actorRuntime, file)),
                $"Retired node-only Actor Directory contract returned: '{file}'."));
        Assert.All(
            new[]
            {
                "ActorDirectoryPartition.cs",
                "ActorDirectoryRange.cs",
                "ActorDirectoryRing.cs",
                "DistributedActorDirectory.cs",
                "StartupActorAffinityDirectory.cs",
                "StartupActorAffinityLayout.cs",
                "ActorLifecycleRpcHandler.cs"
            },
            file =>
            {
                Assert.False(
                    File.Exists(Path.Combine(actorRuntime, file)),
                    $"Cluster-owned Actor adapter returned to the process-local Actors module: '{file}'.");
                Assert.True(
                    File.Exists(Path.Combine(clusterActors, file)),
                    $"Cluster-owned Actor adapter is missing '{file}'.");
            });

        Assert.All(
            new[]
            {
                "ActorLocationCoordinator.cs",
                "ActorLocationDirectory.cs",
                "ActorLocationLayout.cs",
                "ActorLocationShard.cs",
                "IActorLocationStabilizer.cs"
            },
            file => Assert.False(
                File.Exists(Path.Combine(clusterActors, file)),
                $"Retired Actor Location implementation returned: '{file}'."));
    }

    private static HashSet<string> ReadProjectReferenceNames(XDocument project) =>
        project
            .Descendants("ProjectReference")
            .Select(static reference => (string?)reference.Attribute("Include"))
            .Where(static include => !string.IsNullOrWhiteSpace(include))
            .Select(static include =>
                Path.GetFileNameWithoutExtension(include!.Replace('\\', '/')))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> ReadPackageReferenceNames(XDocument project) =>
        project
            .Descendants("PackageReference")
            .Select(static reference => (string?)reference.Attribute("Include"))
            .Where(static include => !string.IsNullOrWhiteSpace(include))
            .Select(static include => include!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
