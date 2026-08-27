using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Planning;
using Xunit;

namespace Lakona.ProjectSystem.Tests.Planning;

public sealed class DependencyPlannerTests
{
    [Fact]
    public void Create_SharedMemoryPack_IncludesContractDependenciesWithoutRpcAdapter()
    {
        var references = DependencyPlanner.Create(ProjectTarget.Shared, Spec(serializer: SerializerKind.MemoryPack)).PackageReferences;

        AssertPackage(references, "Lakona.Rpc.Core");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Game.Abstractions");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Rpc.Serializer.MemoryPack");
        AssertPackage(references, "MemoryPack");
        AssertPackage(references, "MemoryPack.Generator", privateAssets: "all", includeAssets: AnalyzerIncludeAssets);
    }

    [Fact]
    public void Create_SharedJson_StillIncludesMemoryPackForClusterActorContracts()
    {
        var references = DependencyPlanner.Create(
            ProjectTarget.Shared,
            Spec(serializer: SerializerKind.Json)).PackageReferences;

        AssertPackage(references, "Lakona.Rpc.Core");
        AssertPackage(references, "MemoryPack");
        AssertPackage(references, "MemoryPack.Generator", privateAssets: "all", includeAssets: AnalyzerIncludeAssets);
    }

    [Fact]
    public void Create_ServerAppWebSocketJson_IncludesOnlySelectedEndpointRuntimePackages()
    {
        var references = DependencyPlanner.Create(
            ProjectTarget.ServerApp,
            Spec(transport: TransportKind.WebSocket, serializer: SerializerKind.Json)).PackageReferences;

        Assert.DoesNotContain(references, reference => reference.Id == "Microsoft.Extensions.Hosting");
        AssertPackage(references, "Lakona.Game.Server");
        AssertPackage(references, "Microsoft.Extensions.Logging.Console", version: "10.0.0");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Game.Server.Hotfix.Abstractions");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Game.Server.Hotfix");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Game.Server.Hotfix.Generators");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Rpc.Server");
        AssertPackage(references, "Lakona.Rpc.Transport.WebSocket");
        AssertPackage(references, "Lakona.Rpc.Serializer.Json");
        AssertPackage(references, "MemoryPack");
        AssertPackage(references, "MemoryPack.Generator", privateAssets: "all", includeAssets: AnalyzerIncludeAssets);
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Rpc.Transport.Kcp");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Rpc.Transport.Tcp");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Rpc.Serializer.MemoryPack");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Game.Cluster.Rpc.Transport.Tcp");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Game.Cluster.Rpc.Serializer.Json");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Game.Cluster.Rpc.Serializer.MemoryPack");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Game.Cluster");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Game.Cluster.Rpc");
        Assert.DoesNotContain(references, reference => reference.Id is "Dapper" or "MySqlConnector" or "Npgsql");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Rpc.Analyzers");
    }

    [Fact]
    public void Create_ServerAppKcpMemoryPack_IncludesOnlySelectedEndpointPackages()
    {
        var references = DependencyPlanner.Create(
            ProjectTarget.ServerApp,
            Spec(transport: TransportKind.Kcp, serializer: SerializerKind.MemoryPack)).PackageReferences;

        AssertPackage(references, "Lakona.Game.Server");
        AssertPackage(references, "Microsoft.Extensions.Logging.Console", version: "10.0.0");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Game.Cluster.Rpc");
        AssertPackage(references, "Lakona.Rpc.Transport.Kcp");
        AssertPackage(references, "Lakona.Rpc.Serializer.MemoryPack");
        AssertPackage(references, "MemoryPack");
        AssertPackage(references, "MemoryPack.Generator", privateAssets: "all", includeAssets: AnalyzerIncludeAssets);
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Game.Cluster.Rpc.Transport.Tcp");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Game.Cluster.Rpc.Serializer.MemoryPack");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Game.Cluster.Rpc.Serializer.Json");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Rpc.Transport.Tcp");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Rpc.Transport.WebSocket");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Rpc.Serializer.Json");
    }

    [Fact]
    public void Create_UnityKcpMemoryPack_IncludesUnityRuntimeClosure()
    {
        var references = DependencyPlanner.Create(ProjectTarget.UnityClient, Spec()).PackageReferences;

        AssertPackage(references, "Lakona.Rpc.Core");
        AssertPackage(references, "Lakona.Rpc.Client", manuallyInstalled: true);
        AssertPackage(references, "Lakona.Rpc.Transport.Kcp", manuallyInstalled: true);
        AssertPackage(references, "Lakona.Rpc.Serializer.MemoryPack", manuallyInstalled: true);
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Rpc.Analyzers");
        AssertPackage(references, "Lakona.Game.Client");
        AssertPackage(references, "Lakona.Game.Abstractions");
        AssertPackage(references, "System.Threading.Channels", version: "8.0.0");
        AssertPackage(references, "Microsoft.Extensions.Logging.Console", version: "8.0.0");
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
    public void Create_GodotMemoryPack_IncludesOnlyClientSeamAndSelectedAdapters()
    {
        var references = DependencyPlanner.Create(ProjectTarget.GodotClient, Spec(engine: ClientEngine.Godot)).PackageReferences;

        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Rpc.Core");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Rpc.Client");
        AssertPackage(references, "Lakona.Rpc.Transport.Kcp");
        AssertPackage(references, "Lakona.Rpc.Serializer.MemoryPack");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Rpc.Analyzers");
        AssertPackage(references, "Lakona.Game.Client");
        AssertPackage(references, "Microsoft.Extensions.Logging.Console", version: "8.0.0");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Game.Abstractions");
        Assert.DoesNotContain(references, reference => reference.Id is "MemoryPack" or "MemoryPack.Generator");
    }

    [Fact]
    public void Create_ConsoleMemoryPack_HidesTransitiveRpcRuntimeAndAnalyzerPackages()
    {
        var references = DependencyPlanner.Create(ProjectTarget.ConsoleClient, Spec()).PackageReferences;

        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Rpc.Core");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Rpc.Client");
        AssertPackage(references, "Lakona.Rpc.Transport.Kcp");
        AssertPackage(references, "Lakona.Rpc.Serializer.MemoryPack");
        Assert.DoesNotContain(references, reference => reference.Id == "Lakona.Rpc.Analyzers");
        AssertPackage(references, "Lakona.Game.Client");
        AssertPackage(references, "Lakona.Game.LoadTesting");
        AssertPackage(references, "Microsoft.Extensions.Logging.Console", version: "8.0.0");
        Assert.DoesNotContain(references, reference => reference.ManuallyInstalled);
    }

    private const string AnalyzerIncludeAssets = "runtime; build; native; contentfiles; analyzers; buildtransitive";

    private static LakonaProjectSpec Spec(
        ClientEngine engine = ClientEngine.Unity,
        TransportKind transport = TransportKind.Kcp,
        SerializerKind serializer = SerializerKind.MemoryPack)
    {
        var layout = ProjectLayout.Create("MyGame", ".");
        return new LakonaProjectSpec(
            "MyGame",
            layout,
            engine,
            ClientEngineVersionPolicy.GetDefaultVersion(engine),
            transport,
            serializer,
            NuGetForUnitySource.OpenUpm,
            DeploymentProfile.None,
            MembershipProviderKind.Memory,
            ProjectCapabilityCatalog.DefaultCapabilities);
    }

    private static void AssertPackage(
        IReadOnlyList<PackageReferenceSpec> references,
        string id,
        bool? manuallyInstalled = null,
        string? privateAssets = null,
        string? includeAssets = null,
        string? outputItemType = null,
        string? version = null)
    {
        var reference = Assert.Single(references, reference => reference.Id == id);
        Assert.False(string.IsNullOrWhiteSpace(reference.Version));
        if (version is not null)
        {
            Assert.Equal(version, reference.Version);
        }
        if (manuallyInstalled is not null)
        {
            Assert.Equal(manuallyInstalled, reference.ManuallyInstalled);
        }

        Assert.Equal(privateAssets, reference.PrivateAssets);
        Assert.Equal(includeAssets, reference.IncludeAssets);
        Assert.Equal(outputItemType, reference.OutputItemType);
    }
}
