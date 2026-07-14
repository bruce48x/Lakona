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
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Game.Abstractions");
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
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Game.Server.Generators");
        AssertPackage(references, "Lakona.Game.Server.Hotfix");
        AssertPackage(references, "Lakona.Game.Server.Hotfix.Abstractions");
        AssertPackage(references, "Lakona.Game.Server.Hotfix.Generators", privateAssets: "all", outputItemType: "Analyzer");
        AssertPackage(references, "Lakona.Rpc.Server");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Rpc.Transport.WebSocket");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Rpc.Serializer.Json");
        AssertPackage(references, "Lakona.Game.Cluster");
        AssertPackage(references, "Lakona.Game.Cluster.Rpc");
        AssertPackage(references, "Dapper");
        AssertPackage(references, "Npgsql");
        AssertPackage(references, "Lakona.Rpc.Analyzers", privateAssets: "all", includeAssets: AnalyzerIncludeAssets);
    }

    [Fact]
    public void Create_ServerAppMemoryPack_DoesNotReferenceClusterMemoryPackDirectly()
    {
        var references = DependencyPlanner.Create(
            ProjectTarget.ServerApp,
            Spec(transport: TransportKind.Tcp, serializer: SerializerKind.MemoryPack)).PackageReferences;

        AssertPackage(references, "Lakona.Game.Server");
        AssertPackage(references, "Lakona.Game.Cluster.Rpc");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Game.Cluster.Rpc.MemoryPack");
    }

    [Fact]
    public void Create_ServerHotfix_IncludesHotfixGeneratorAnalyzer()
    {
        var plan = DependencyPlanner.Create(ProjectTarget.ServerHotfix, Spec());

        AssertPackage(plan.PackageReferences, "Lakona.Game.Server.Hotfix.Generators", privateAssets: "all", outputItemType: "Analyzer");
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
        AssertPackage(references, "Microsoft.Extensions.Logging.Console");
        AssertPackage(references, "Microsoft.Extensions.Logging");
        AssertPackage(references, "Microsoft.Extensions.Logging.Abstractions");
        AssertPackage(references, "Microsoft.Extensions.DependencyInjection");
        AssertPackage(references, "Microsoft.Extensions.DependencyInjection.Abstractions");
        AssertPackage(references, "Microsoft.Extensions.Logging.Configuration");
        AssertPackage(references, "Microsoft.Extensions.Options");
        AssertPackage(references, "Microsoft.Extensions.Options.ConfigurationExtensions");
        AssertPackage(references, "Microsoft.Extensions.Configuration");
        AssertPackage(references, "Microsoft.Extensions.Configuration.Abstractions");
        AssertPackage(references, "Microsoft.Extensions.Configuration.Binder");
        AssertPackage(references, "Microsoft.Extensions.Primitives");
        AssertPackage(references, "Microsoft.Bcl.AsyncInterfaces");
        AssertPackage(references, "System.Diagnostics.DiagnosticSource");
        AssertPackage(references, "System.ComponentModel.Annotations");
        AssertPackage(references, "System.Text.Json");
        AssertPackage(references, "System.Text.Encodings.Web");
        AssertPackage(references, "System.Buffers");
        AssertPackage(references, "Kcp");
        AssertPackage(references, "MemoryPack");
        AssertPackage(references, "MemoryPack.Core");
        AssertPackage(references, "Microsoft.CodeAnalysis.CSharp");
    }

    [Fact]
    public void Create_GodotMemoryPack_IncludesSelectedSerializerButDoesNotDuplicateSharedOwnedMemoryPackPackages()
    {
        var references = DependencyPlanner.Create(ProjectTarget.GodotClient, Spec(engine: ClientEngine.Godot)).PackageReferences;

        AssertPackage(references, "Lakona.Rpc.Core");
        AssertPackage(references, "Lakona.Rpc.Client");
        AssertPackage(references, "Lakona.Rpc.Transport.Kcp");
        AssertPackage(references, "Lakona.Rpc.Serializer.MemoryPack");
        AssertPackage(references, "Lakona.Rpc.Analyzers", privateAssets: "all", includeAssets: AnalyzerIncludeAssets);
        AssertPackage(references, "Lakona.Game.Client");
        AssertPackage(references, "Lakona.Game.Abstractions");
        Assert.DoesNotContain(references, reference => reference.Id is "MemoryPack" or "MemoryPack.Generator");
    }

    [Fact]
    public void Create_ConsoleMemoryPack_IncludesSdkClientLoadTestingAndAnalyzerPackages()
    {
        var references = DependencyPlanner.Create(ProjectTarget.ConsoleClient, Spec()).PackageReferences;

        AssertPackage(references, "Lakona.Rpc.Core");
        AssertPackage(references, "Lakona.Rpc.Client");
        AssertPackage(references, "Lakona.Rpc.Transport.Kcp");
        AssertPackage(references, "Lakona.Rpc.Serializer.MemoryPack");
        AssertPackage(references, "Lakona.Rpc.Analyzers", privateAssets: "all", includeAssets: AnalyzerIncludeAssets);
        AssertPackage(references, "Lakona.Game.Client");
        AssertPackage(references, "Lakona.Game.LoadTesting");
        Assert.DoesNotContain(references, reference => reference.ManuallyInstalled);
    }

    private const string AnalyzerIncludeAssets = "runtime; build; native; contentfiles; analyzers; buildtransitive";

    private static LakonaProjectSpec Spec(
        ClientEngine engine = ClientEngine.Unity,
        TransportKind transport = TransportKind.Kcp,
        SerializerKind serializer = SerializerKind.MemoryPack,
        PersistenceKind persistence = PersistenceKind.None)
    {
        var layout = ProjectLayout.Create("MyGame", ".");
        return new LakonaProjectSpec(
            "MyGame",
            layout,
            engine,
            ClientEngineVersionPolicy.GetDefaultVersion(engine),
            transport,
            serializer,
            persistence,
            NuGetForUnitySource.OpenUpm,
            DeploymentProfile.None,
            ProjectCapabilityCatalog.DefaultCapabilities);
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
