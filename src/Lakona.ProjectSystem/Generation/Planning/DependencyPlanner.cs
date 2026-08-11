using Lakona.ProjectSystem.Generation.Domain;
using DomainPackageCatalog = Lakona.ProjectSystem.Generation.Domain.PackageCatalog;

namespace Lakona.ProjectSystem.Generation.Planning;

internal enum ProjectTarget
{
    Shared,
    ServerApp,
    UnityClient,
    GodotClient,
    ConsoleClient
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
            ProjectTarget.UnityClient => CreateUnityClientPlan(spec, catalog),
            ProjectTarget.GodotClient => CreateGodotClientPlan(spec, catalog),
            ProjectTarget.ConsoleClient => CreateConsoleClientPlan(spec, catalog),
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

        references.Add(Sdk("MemoryPack", catalog.MemoryPack));
        references.Add(Sdk("MemoryPack.Generator", catalog.MemoryPack, privateAssets: "all", includeAssets: AnalyzerIncludeAssets));

        return references;
    }

    private static IReadOnlyList<PackageReferenceSpec> CreateServerAppPlan(LakonaProjectSpec spec, DomainPackageCatalog catalog)
    {
        var references = new List<PackageReferenceSpec>
        {
            Sdk("Lakona.Game.Server", catalog.LakonaGameServer),
            Sdk("Microsoft.Extensions.Logging.Console", catalog.MicrosoftExtensionsLoggingConsoleServer),
            Sdk(GetTransportPackage(spec.Transport), GetTransportVersion(spec.Transport, catalog)),
            Sdk(GetSerializerPackage(spec.Serializer), GetSerializerVersion(spec.Serializer, catalog)),
            Sdk("MemoryPack", catalog.MemoryPack),
            Sdk("MemoryPack.Generator", catalog.MemoryPack, privateAssets: "all", includeAssets: AnalyzerIncludeAssets)
        };

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
            Unity("Lakona.Game.Client", catalog.LakonaGameClient),
            Unity("Lakona.Game.Abstractions", catalog.LakonaGameAbstractions),
            Unity("System.Threading.Channels", catalog.SystemThreadingChannels),
            Unity("Microsoft.Extensions.Logging.Console", catalog.MicrosoftExtensionsLoggingConsole),
            Unity("Microsoft.Extensions.Logging", catalog.MicrosoftExtensionsLogging),
            Unity("Microsoft.Extensions.Logging.Abstractions", catalog.MicrosoftExtensionsLoggingAbstractions),
            Unity("Microsoft.Extensions.Logging.Configuration", catalog.MicrosoftExtensionsLoggingConfiguration),
            Unity("Microsoft.Extensions.DependencyInjection", catalog.MicrosoftExtensionsDependencyInjection),
            Unity("Microsoft.Extensions.DependencyInjection.Abstractions", catalog.MicrosoftExtensionsDependencyInjectionAbstractions),
            Unity("Microsoft.Extensions.Configuration", catalog.MicrosoftExtensionsConfiguration),
            Unity("Microsoft.Extensions.Configuration.Abstractions", catalog.MicrosoftExtensionsConfigurationAbstractions),
            Unity("Microsoft.Extensions.Configuration.Binder", catalog.MicrosoftExtensionsConfigurationBinder),
            Unity("Microsoft.Extensions.Options", catalog.MicrosoftExtensionsOptions),
            Unity("Microsoft.Extensions.Options.ConfigurationExtensions", catalog.MicrosoftExtensionsOptionsConfigurationExtensions),
            Unity("Microsoft.Extensions.Primitives", catalog.MicrosoftExtensionsPrimitives),
            Unity("Microsoft.Bcl.AsyncInterfaces", catalog.MicrosoftBclAsyncInterfaces),
            Unity("System.Diagnostics.DiagnosticSource", catalog.SystemDiagnosticsDiagnosticSource),
            Unity("System.ComponentModel.Annotations", catalog.SystemComponentModelAnnotations),
            Unity("System.Text.Json", catalog.SystemTextJson),
            Unity("System.Text.Encodings.Web", catalog.SystemTextEncodingsWeb),
            Unity("System.Buffers", catalog.SystemBuffers)
        };

        if (spec.Transport == TransportKind.Kcp)
        {
            references.Add(Unity("Kcp", catalog.Kcp));
            references.Add(Unity("System.Memory", catalog.SystemMemoryForKcp));
            references.Add(Unity("System.Threading.Tasks.Extensions", catalog.SystemThreadingTasksExtensionsForKcp));
        }

        AddUnitySerializerDependencies(spec, catalog, references);
        return references
            .DistinctBy(static reference => reference.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<PackageReferenceSpec> CreateGodotClientPlan(LakonaProjectSpec spec, DomainPackageCatalog catalog)
    {
        var references = new List<PackageReferenceSpec>
        {
            Sdk(GetTransportPackage(spec.Transport), GetTransportVersion(spec.Transport, catalog)),
            Sdk(GetSerializerPackage(spec.Serializer), GetSerializerVersion(spec.Serializer, catalog)),
            Sdk("Lakona.Game.Client", catalog.LakonaGameClient),
            Sdk("Microsoft.Extensions.Logging.Console", catalog.MicrosoftExtensionsLoggingConsole)
        };

        return references;
    }

    private static IReadOnlyList<PackageReferenceSpec> CreateConsoleClientPlan(LakonaProjectSpec spec, DomainPackageCatalog catalog)
    {
        return
        [
            Sdk(GetTransportPackage(spec.Transport), GetTransportVersion(spec.Transport, catalog)),
            Sdk(GetSerializerPackage(spec.Serializer), GetSerializerVersion(spec.Serializer, catalog)),
            Sdk("Lakona.Game.Client", catalog.LakonaGameClient),
            Sdk("Lakona.Game.LoadTesting", catalog.LakonaGameLoadTesting),
            Sdk("Microsoft.Extensions.Logging.Console", catalog.MicrosoftExtensionsLoggingConsole)
        ];
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
