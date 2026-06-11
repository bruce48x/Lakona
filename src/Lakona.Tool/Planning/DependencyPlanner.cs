using Lakona.Tool.Domain;
using DomainPackageCatalog = Lakona.Tool.Domain.PackageCatalog;

namespace Lakona.Tool.Planning;

internal enum ProjectTarget
{
    Shared,
    ServerApp,
    ServerHotfix,
    UnityClient,
    GodotClient
}

internal static class DependencyPlanner
{
    private const string AnalyzerIncludeAssets = "runtime; build; native; contentfiles; analyzers; buildtransitive";

    public static DependencyPlan Create(ProjectTarget target, LakonaProjectSpec spec)
    {
        var catalog = new DomainPackageCatalog();
        var references = target switch
        {
            ProjectTarget.Shared => CreateSharedPlan(spec, catalog),
            ProjectTarget.ServerApp => CreateServerAppPlan(spec, catalog),
            ProjectTarget.ServerHotfix => [],
            ProjectTarget.UnityClient => CreateUnityClientPlan(spec, catalog),
            ProjectTarget.GodotClient => CreateGodotClientPlan(spec, catalog),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null)
        };

        return new DependencyPlan(references);
    }

    private static IReadOnlyList<PackageReferenceSpec> CreateSharedPlan(LakonaProjectSpec spec, DomainPackageCatalog catalog)
    {
        var references = new List<PackageReferenceSpec>
        {
            Sdk("Lakona.Rpc.Core", catalog.LakonaRpcCore)
        };

        if (spec.Serializer == SerializerKind.MemoryPack)
        {
            references.Add(Sdk("Lakona.Rpc.Serializer.MemoryPack", catalog.LakonaRpcSerializerMemoryPack));
            references.Add(Sdk("MemoryPack", catalog.MemoryPack));
            references.Add(Sdk("MemoryPack.Generator", catalog.MemoryPack, privateAssets: "all", includeAssets: AnalyzerIncludeAssets));
        }

        return references;
    }

    private static IReadOnlyList<PackageReferenceSpec> CreateServerAppPlan(LakonaProjectSpec spec, DomainPackageCatalog catalog)
    {
        var references = new List<PackageReferenceSpec>
        {
            Sdk("Microsoft.Extensions.Hosting", catalog.MicrosoftExtensionsHosting),
            Sdk("Lakona.Game.Server", catalog.LakonaGameServer),
            Sdk("Lakona.Game.Server.Generators", catalog.LakonaGameServerGenerators, privateAssets: "all", outputItemType: "Analyzer"),
            Sdk("Lakona.Game.Server.Hotfix", catalog.LakonaGameServerHotfix),
            Sdk("Lakona.Game.Server.Hotfix.Generators", catalog.LakonaGameServerHotfixGenerators, privateAssets: "all", outputItemType: "Analyzer"),
            Sdk("Lakona.Rpc.Server", catalog.LakonaRpcServer),
            Sdk(GetTransportPackage(spec.Transport), GetTransportVersion(spec.Transport, catalog)),
            Sdk("Lakona.Rpc.Analyzers", catalog.LakonaRpcAnalyzers, privateAssets: "all", includeAssets: AnalyzerIncludeAssets),
            Sdk("Lakona.Game.Cluster", catalog.LakonaGameCluster),
            Sdk("Lakona.Game.Cluster.Rpc", catalog.LakonaGameClusterRpc)
        };

        if (spec.Serializer == SerializerKind.Json)
        {
            references.Add(Sdk("Lakona.Rpc.Serializer.Json", catalog.LakonaRpcSerializerJson));
        }

        if (spec.Persistence != PersistenceKind.None)
        {
            references.Add(Sdk("Dapper", catalog.Dapper));
            references.Add(spec.Persistence == PersistenceKind.MySql
                ? Sdk("MySqlConnector", catalog.MySqlConnector)
                : Sdk("Npgsql", catalog.Npgsql));
        }

        return references;
    }

    private static IReadOnlyList<PackageReferenceSpec> CreateUnityClientPlan(LakonaProjectSpec spec, DomainPackageCatalog catalog)
    {
        var references = new List<PackageReferenceSpec>
        {
            Unity("Lakona.Rpc.Core", catalog.LakonaRpcCore),
            Unity("Lakona.Rpc.Client", catalog.LakonaRpcClient, manuallyInstalled: true),
            Unity(GetTransportPackage(spec.Transport), GetTransportVersion(spec.Transport, catalog), manuallyInstalled: true),
            Unity(GetSerializerPackage(spec.Serializer), GetSerializerVersion(spec.Serializer, catalog), manuallyInstalled: true),
            Unity("Lakona.Rpc.Analyzers", catalog.LakonaRpcAnalyzers, manuallyInstalled: true),
            Unity("Lakona.Game.Client", catalog.LakonaGameClient),
            Unity("Lakona.Game.Abstractions", catalog.LakonaGameAbstractions),
            Unity("System.Threading.Channels", catalog.SystemThreadingChannels)
        };

        if (spec.Transport == TransportKind.Kcp)
        {
            references.Add(Unity("Kcp", catalog.Kcp));
            references.Add(Unity("System.Memory", catalog.SystemMemoryForKcp));
            references.Add(Unity("System.Threading.Tasks.Extensions", catalog.SystemThreadingTasksExtensionsForKcp));
        }

        AddUnitySerializerDependencies(spec, catalog, references);
        return references;
    }

    private static IReadOnlyList<PackageReferenceSpec> CreateGodotClientPlan(LakonaProjectSpec spec, DomainPackageCatalog catalog)
    {
        var references = new List<PackageReferenceSpec>
        {
            Sdk("Lakona.Rpc.Core", catalog.LakonaRpcCore),
            Sdk("Lakona.Rpc.Client", catalog.LakonaRpcClient),
            Sdk(GetTransportPackage(spec.Transport), GetTransportVersion(spec.Transport, catalog)),
            Sdk(GetSerializerPackage(spec.Serializer), GetSerializerVersion(spec.Serializer, catalog)),
            Sdk("Lakona.Rpc.Analyzers", catalog.LakonaRpcAnalyzers, privateAssets: "all", includeAssets: AnalyzerIncludeAssets),
            Sdk("Lakona.Game.Client", catalog.LakonaGameClient)
        };

        return references;
    }

    private static void AddUnitySerializerDependencies(
        LakonaProjectSpec spec,
        DomainPackageCatalog catalog,
        List<PackageReferenceSpec> references)
    {
        if (spec.Serializer == SerializerKind.Json)
        {
            references.Add(Unity("Microsoft.Bcl.AsyncInterfaces", catalog.MicrosoftBclAsyncInterfaces));
            references.Add(Unity("System.IO.Pipelines", catalog.SystemIoPipelinesForJson));
            references.Add(Unity("System.Text.Encodings.Web", catalog.SystemTextEncodingsWeb));
            references.Add(Unity("System.Buffers", catalog.SystemBuffers));
            references.Add(Unity("System.Memory", catalog.SystemMemoryForJson));
            references.Add(Unity("System.Runtime.CompilerServices.Unsafe", catalog.SystemRuntimeCompilerServicesUnsafe));
            references.Add(Unity("System.Threading.Tasks.Extensions", catalog.SystemThreadingTasksExtensionsForJson));
            references.Add(Unity("System.Text.Json", catalog.SystemTextJson));
            return;
        }

        references.Add(Unity("MemoryPack", catalog.MemoryPack));
        references.Add(Unity("MemoryPack.Core", catalog.MemoryPackCore));
        references.Add(Unity("MemoryPack.Generator", catalog.MemoryPack));
        references.Add(Unity("Microsoft.CodeAnalysis.Common", catalog.MicrosoftCodeAnalysisCommon));
        references.Add(Unity("Microsoft.CodeAnalysis.CSharp", catalog.MicrosoftCodeAnalysisCSharp));
        references.Add(Unity("System.Collections.Immutable", catalog.SystemCollectionsImmutable));
        references.Add(Unity("System.Reflection.Metadata", catalog.SystemReflectionMetadata));
        references.Add(Unity("System.Text.Encoding.CodePages", catalog.SystemTextEncodingCodePages));
        references.Add(Unity("System.Threading.Tasks.Extensions", catalog.SystemThreadingTasksExtensionsForRoslyn));
        references.Add(Unity("System.Memory", catalog.SystemMemoryForRoslyn));
        references.Add(Unity("System.Runtime.CompilerServices.Unsafe", catalog.SystemRuntimeCompilerServicesUnsafe));
        references.Add(Unity("System.IO.Pipelines", catalog.SystemIoPipelines));
    }

    private static string GetTransportPackage(TransportKind transport) => transport switch
    {
        TransportKind.Tcp => "Lakona.Rpc.Transport.Tcp",
        TransportKind.WebSocket => "Lakona.Rpc.Transport.WebSocket",
        TransportKind.Kcp => "Lakona.Rpc.Transport.Kcp",
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
    };

    private static string GetTransportVersion(TransportKind transport, DomainPackageCatalog catalog) => transport switch
    {
        TransportKind.Tcp => catalog.LakonaRpcTransportTcp,
        TransportKind.WebSocket => catalog.LakonaRpcTransportWebSocket,
        TransportKind.Kcp => catalog.LakonaRpcTransportKcp,
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
    };

    private static string GetSerializerPackage(SerializerKind serializer) => serializer switch
    {
        SerializerKind.Json => "Lakona.Rpc.Serializer.Json",
        SerializerKind.MemoryPack => "Lakona.Rpc.Serializer.MemoryPack",
        _ => throw new ArgumentOutOfRangeException(nameof(serializer), serializer, null)
    };

    private static string GetSerializerVersion(SerializerKind serializer, DomainPackageCatalog catalog) => serializer switch
    {
        SerializerKind.Json => catalog.LakonaRpcSerializerJson,
        SerializerKind.MemoryPack => catalog.LakonaRpcSerializerMemoryPack,
        _ => throw new ArgumentOutOfRangeException(nameof(serializer), serializer, null)
    };

    private static PackageReferenceSpec Sdk(
        string id,
        string version,
        string? privateAssets = null,
        string? includeAssets = null,
        string? outputItemType = null) =>
        new(id, version, PackageReferenceStyle.Sdk, PrivateAssets: privateAssets, IncludeAssets: includeAssets, OutputItemType: outputItemType);

    private static PackageReferenceSpec Unity(string id, string version, bool manuallyInstalled = false) =>
        new(id, version, PackageReferenceStyle.NuGetForUnity, manuallyInstalled);
}
