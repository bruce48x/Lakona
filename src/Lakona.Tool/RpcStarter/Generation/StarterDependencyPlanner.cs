namespace Lakona.Tool.RpcStarter;

internal enum StarterProjectRole
{
    Shared,
    Server,
    UnityClient,
    GodotClient,
    ConsoleClient
}

internal sealed record StarterPackageReference(
    string Id,
    string Version,
    bool ManuallyInstalled = false,
    string? PrivateAssets = null,
    string? IncludeAssets = null);

internal sealed record StarterDependencyPlan(IReadOnlyList<StarterPackageReference> PackageReferences);

internal static class StarterDependencyPlanner
{
    private const string AnalyzerIncludeAssets = "runtime; build; native; contentfiles; analyzers; buildtransitive";

    public static StarterDependencyPlan Create(StarterTemplateContext context, StarterProjectRole role)
    {
        var references = role switch
        {
            StarterProjectRole.Shared => CreateSharedPlan(context),
            StarterProjectRole.Server => CreateServerPlan(context),
            StarterProjectRole.UnityClient => CreateUnityClientPlan(context),
            StarterProjectRole.GodotClient => CreateGodotClientPlan(context),
            StarterProjectRole.ConsoleClient => CreateConsoleClientPlan(context),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
        };

        return new StarterDependencyPlan(references);
    }

    private static IReadOnlyList<StarterPackageReference> CreateSharedPlan(StarterTemplateContext context)
    {
        var references = new List<StarterPackageReference>
        {
            new("Lakona.Rpc.Core", context.Versions.Core)
        };

        if (context.Serializer == SerializerKind.MemoryPack)
        {
            references.Add(new("Lakona.Rpc.Serializer.MemoryPack", context.Versions.Serializer));
            references.Add(new("MemoryPack", RequireVersion(context.Versions.SerializerRuntime, "MemoryPack")));
            references.Add(new(
                "MemoryPack.Generator",
                RequireVersion(context.Versions.SerializerRuntime, "MemoryPack.Generator"),
                PrivateAssets: "all",
                IncludeAssets: "runtime; build; native; contentfiles; analyzers; buildtransitive"));
        }

        return references;
    }

    private static IReadOnlyList<StarterPackageReference> CreateServerPlan(StarterTemplateContext context)
    {
        var references = new List<StarterPackageReference>
        {
            new("Lakona.Rpc.Server", context.Versions.Server),
            new(NuGetVersionResolver.GetTransportPackage(context.Transport), context.Versions.Transport),
            CreateAnalyzerReference(context)
        };

        AddSdkSerializerReferenceIfNeeded(context, references);
        return references;
    }

    private static IReadOnlyList<StarterPackageReference> CreateGodotClientPlan(StarterTemplateContext context)
    {
        var references = new List<StarterPackageReference>
        {
            new("Lakona.Rpc.Core", context.Versions.Core),
            new("Lakona.Rpc.Client", context.Versions.Client),
            new(NuGetVersionResolver.GetTransportPackage(context.Transport), context.Versions.Transport),
            CreateAnalyzerReference(context)
        };

        AddSdkSerializerReferenceIfNeeded(context, references);
        return references;
    }

    private static IReadOnlyList<StarterPackageReference> CreateConsoleClientPlan(StarterTemplateContext context)
    {
        var references = new List<StarterPackageReference>
        {
            new("Lakona.Rpc.Core", context.Versions.Core),
            new("Lakona.Rpc.Client", context.Versions.Client),
            new(NuGetVersionResolver.GetTransportPackage(context.Transport), context.Versions.Transport),
            CreateAnalyzerReference(context)
        };

        AddSdkSerializerReferenceIfNeeded(context, references);
        return references;
    }

    private static IReadOnlyList<StarterPackageReference> CreateUnityClientPlan(StarterTemplateContext context)
    {
        var references = new List<StarterPackageReference>
        {
            new("Lakona.Rpc.Core", context.Versions.Core),
            new("Lakona.Rpc.Client", context.Versions.Client, ManuallyInstalled: true),
            new(NuGetVersionResolver.GetTransportPackage(context.Transport), context.Versions.Transport, ManuallyInstalled: true),
            new(NuGetVersionResolver.GetSerializerPackage(context.Serializer), context.Versions.Serializer, ManuallyInstalled: true),
            new("Lakona.Rpc.Analyzers", context.Versions.Analyzers, ManuallyInstalled: true),
            new("System.Threading.Channels", UnityPackageVersions.SystemThreadingChannels)
        };

        if (context.Transport == TransportKind.Kcp)
        {
            references.Add(new("Kcp", UnityPackageVersions.Kcp));
            references.Add(new("System.Memory", UnityPackageVersions.SystemMemoryForKcp));
            references.Add(new("System.Threading.Tasks.Extensions", UnityPackageVersions.SystemThreadingTasksExtensionsForKcp));
        }

        AddUnitySerializerDependencies(context, references);
        return references;
    }

    private static void AddSdkSerializerReferenceIfNeeded(
        StarterTemplateContext context,
        List<StarterPackageReference> references)
    {
        switch (context.Serializer)
        {
            case SerializerKind.Json:
                references.Add(new(NuGetVersionResolver.GetSerializerPackage(context.Serializer), context.Versions.Serializer));
                break;
            case SerializerKind.MemoryPack:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(context), context.Serializer, null);
        }
    }

    private static StarterPackageReference CreateAnalyzerReference(StarterTemplateContext context) =>
        new(
            "Lakona.Rpc.Analyzers",
            context.Versions.Analyzers,
            PrivateAssets: "all",
            IncludeAssets: AnalyzerIncludeAssets);

    private static void AddUnitySerializerDependencies(
        StarterTemplateContext context,
        List<StarterPackageReference> references)
    {
        switch (context.Serializer)
        {
            case SerializerKind.Json:
                references.Add(new("Microsoft.Bcl.AsyncInterfaces", UnityPackageVersions.MicrosoftBclAsyncInterfaces));
                references.Add(new("System.IO.Pipelines", UnityPackageVersions.SystemIoPipelinesForJson));
                references.Add(new("System.Text.Encodings.Web", UnityPackageVersions.SystemTextEncodingsWeb));
                references.Add(new("System.Buffers", UnityPackageVersions.SystemBuffers));
                references.Add(new("System.Memory", UnityPackageVersions.SystemMemoryForJson));
                references.Add(new("System.Runtime.CompilerServices.Unsafe", UnityPackageVersions.SystemRuntimeCompilerServicesUnsafe));
                references.Add(new("System.Threading.Tasks.Extensions", UnityPackageVersions.SystemThreadingTasksExtensionsForJson));
                references.Add(new("System.Text.Json", UnityPackageVersions.SystemTextJson));
                break;
            case SerializerKind.MemoryPack:
                references.Add(new("MemoryPack", RequireVersion(context.Versions.SerializerRuntime, "MemoryPack")));
                references.Add(new("MemoryPack.Core", RequireVersion(context.Versions.SerializerRuntimeCore, "MemoryPack.Core")));
                references.Add(new("MemoryPack.Generator", RequireVersion(context.Versions.SerializerRuntime, "MemoryPack.Generator")));
                references.Add(new("Microsoft.CodeAnalysis.Common", UnityPackageVersions.MicrosoftCodeAnalysisCommon));
                references.Add(new("Microsoft.CodeAnalysis.CSharp", UnityPackageVersions.MicrosoftCodeAnalysisCSharp));
                references.Add(new("System.Collections.Immutable", UnityPackageVersions.SystemCollectionsImmutable));
                references.Add(new("System.Reflection.Metadata", UnityPackageVersions.SystemReflectionMetadata));
                references.Add(new("System.Text.Encoding.CodePages", UnityPackageVersions.SystemTextEncodingCodePages));
                references.Add(new("System.Threading.Tasks.Extensions", UnityPackageVersions.SystemThreadingTasksExtensionsForRoslyn));
                references.Add(new("System.Memory", UnityPackageVersions.SystemMemoryForRoslyn));
                references.Add(new("System.Runtime.CompilerServices.Unsafe", UnityPackageVersions.SystemRuntimeCompilerServicesUnsafe));
                references.Add(new("System.IO.Pipelines", UnityPackageVersions.SystemIoPipelines));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(context), context.Serializer, null);
        }
    }

    private static string RequireVersion(string? version, string packageId)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException($"{packageId} requires an explicit starter package version, but none was resolved.");
        }

        return version;
    }

}
