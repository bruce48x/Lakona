using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Xunit;

namespace Lakona.Tool.Tests.Planning;

public sealed class DependencyPlannerTests
{
    [Fact]
    public void Create_SharedMemoryPack_IncludesSerializerRuntimeAndGenerator()
    {
        var references = DependencyPlanner.Create(ProjectTarget.Shared, Spec(serializer: SerializerKind.MemoryPack)).PackageReferences;

        AssertPackage(references, "Lakona.Rpc.Core");
        AssertPackage(references, "Lakona.Rpc.Serializer.MemoryPack");
        AssertPackage(references, "MemoryPack");
        AssertPackage(references, "MemoryPack.Generator", privateAssets: "all", includeAssets: AnalyzerIncludeAssets);
    }

    [Fact]
    public void Create_ServerAppJsonPostgres_IncludesGameRpcClusterPersistenceAndAnalyzerPackages()
    {
        var references = DependencyPlanner.Create(
            ProjectTarget.ServerApp,
            Spec(transport: TransportKind.WebSocket, serializer: SerializerKind.Json, persistence: PersistenceKind.Postgres)).PackageReferences;

        AssertPackage(references, "Microsoft.Extensions.Hosting");
        AssertPackage(references, "Lakona.Game.Server");
        AssertPackage(references, "Lakona.Game.Server.Generators", privateAssets: "all", outputItemType: "Analyzer");
        AssertPackage(references, "Lakona.Game.Server.Hotfix");
        AssertPackage(references, "Lakona.Game.Server.Hotfix.Generators", privateAssets: "all", outputItemType: "Analyzer");
        AssertPackage(references, "Lakona.Rpc.Server");
        AssertPackage(references, "Lakona.Rpc.Transport.WebSocket");
        AssertPackage(references, "Lakona.Rpc.Serializer.Json");
        AssertPackage(references, "Lakona.Game.Cluster");
        AssertPackage(references, "Lakona.Game.Cluster.Rpc");
        AssertPackage(references, "Dapper");
        AssertPackage(references, "Npgsql");
        AssertPackage(references, "Lakona.Rpc.Analyzers", privateAssets: "all", includeAssets: AnalyzerIncludeAssets);
    }

    [Fact]
    public void Create_UnityKcpMemoryPack_IncludesUnityRuntimeClosure()
    {
        var references = DependencyPlanner.Create(ProjectTarget.UnityClient, Spec()).PackageReferences;

        AssertPackage(references, "Lakona.Rpc.Core");
        AssertPackage(references, "Lakona.Rpc.Client", manuallyInstalled: true);
        AssertPackage(references, "Lakona.Rpc.Transport.Kcp", manuallyInstalled: true);
        AssertPackage(references, "Lakona.Rpc.Serializer.MemoryPack", manuallyInstalled: true);
        AssertPackage(references, "Lakona.Rpc.Analyzers", manuallyInstalled: true);
        AssertPackage(references, "Lakona.Game.Client");
        AssertPackage(references, "Lakona.Game.Abstractions");
        AssertPackage(references, "System.Threading.Channels");
        AssertPackage(references, "Kcp");
        AssertPackage(references, "MemoryPack");
        AssertPackage(references, "MemoryPack.Core");
        AssertPackage(references, "Microsoft.CodeAnalysis.CSharp");
    }

    [Fact]
    public void Create_GodotMemoryPack_IncludesSelectedSerializerButDoesNotDuplicateSharedOwnedMemoryPackPackages()
    {
        var references = DependencyPlanner.Create(ProjectTarget.GodotClient, Spec()).PackageReferences;

        AssertPackage(references, "Lakona.Rpc.Core");
        AssertPackage(references, "Lakona.Rpc.Client");
        AssertPackage(references, "Lakona.Rpc.Transport.Kcp");
        AssertPackage(references, "Lakona.Rpc.Serializer.MemoryPack");
        AssertPackage(references, "Lakona.Rpc.Analyzers", privateAssets: "all", includeAssets: AnalyzerIncludeAssets);
        AssertPackage(references, "Lakona.Game.Client");
        Assert.DoesNotContain(references, reference => reference.Id is "MemoryPack" or "MemoryPack.Generator");
    }

    private const string AnalyzerIncludeAssets = "runtime; build; native; contentfiles; analyzers; buildtransitive";

    private static LakonaProjectSpec Spec(
        TransportKind transport = TransportKind.Kcp,
        SerializerKind serializer = SerializerKind.MemoryPack,
        PersistenceKind persistence = PersistenceKind.None)
    {
        var layout = ProjectLayout.Create("MyGame", ".");
        return new LakonaProjectSpec(
            "MyGame",
            layout,
            ClientEngine.Unity,
            transport,
            serializer,
            persistence,
            NuGetForUnitySource.OpenUpm,
            DeploymentProfile.None,
            ProjectFeatureCatalog.DefaultFeatures);
    }

    private static void AssertPackage(
        IReadOnlyList<PackageReferenceSpec> references,
        string id,
        bool? manuallyInstalled = null,
        string? privateAssets = null,
        string? includeAssets = null,
        string? outputItemType = null)
    {
        var reference = Assert.Single(references, reference => reference.Id == id);
        Assert.False(string.IsNullOrWhiteSpace(reference.Version));
        if (manuallyInstalled is not null)
        {
            Assert.Equal(manuallyInstalled, reference.ManuallyInstalled);
        }

        Assert.Equal(privateAssets, reference.PrivateAssets);
        Assert.Equal(includeAssets, reference.IncludeAssets);
        Assert.Equal(outputItemType, reference.OutputItemType);
    }
}
