using Lakona.Tool.RpcStarter;
using Xunit;

namespace Lakona.Tool.Tests.RpcStarter;

public sealed class StarterDependencyPlannerTests
{
    private static readonly ResolvedVersions Versions = new("1.2.3", "2.3.4", "3.4.5", "4.5.6", "5.6.7", "0.1.2", "7.8.9", "8.9.10");

    [Fact]
    public void SharedMemoryPack_IncludesSerializerRuntimeAndGenerator()
    {
        var ids = CreateIds(StarterProjectRole.Shared, SerializerKind.MemoryPack);

        Assert.Contains("Lakona.Rpc.Core", ids);
        Assert.Contains("Lakona.Rpc.Serializer.MemoryPack", ids);
        Assert.Contains("MemoryPack", ids);
        Assert.Contains("MemoryPack.Generator", ids);
    }

    [Fact]
    public void SharedJson_DoesNotIncludeJsonSerializer()
    {
        var ids = CreateIds(StarterProjectRole.Shared, SerializerKind.Json);

        Assert.Contains("Lakona.Rpc.Core", ids);
        Assert.DoesNotContain("Lakona.Rpc.Serializer.Json", ids);
    }

    [Fact]
    public void ServerMemoryPack_DoesNotRepeatSharedSerializerDependencies()
    {
        var ids = CreateIds(StarterProjectRole.Server, SerializerKind.MemoryPack);

        Assert.Contains("Lakona.Rpc.Server", ids);
        Assert.Contains("Lakona.Rpc.Transport.WebSocket", ids);
        Assert.DoesNotContain("Lakona.Rpc.Serializer.MemoryPack", ids);
        Assert.DoesNotContain("MemoryPack", ids);
        Assert.DoesNotContain("MemoryPack.Generator", ids);
        Assert.Contains("Lakona.Rpc.Analyzers", ids);
    }

    [Fact]
    public void ServerJson_IncludesJsonSerializer()
    {
        var plan = CreatePlan(StarterProjectRole.Server, SerializerKind.Json);
        var ids = plan.PackageReferences.Select(static reference => reference.Id).ToArray();

        Assert.Contains("Lakona.Rpc.Serializer.Json", ids);
        Assert.Contains("Lakona.Rpc.Analyzers", ids);
    }

    [Fact]
    public void GodotMemoryPack_DoesNotRepeatSharedSerializerDependencies()
    {
        var ids = CreateIds(StarterProjectRole.GodotClient, SerializerKind.MemoryPack, ClientEngineKind.Godot);

        Assert.Contains("Lakona.Rpc.Core", ids);
        Assert.Contains("Lakona.Rpc.Client", ids);
        Assert.Contains("Lakona.Rpc.Transport.WebSocket", ids);
        Assert.Contains("Lakona.Rpc.Analyzers", ids);
        Assert.DoesNotContain("Lakona.Rpc.Serializer.MemoryPack", ids);
        Assert.DoesNotContain("MemoryPack", ids);
        Assert.DoesNotContain("MemoryPack.Core", ids);
    }

    [Fact]
    public void GodotJson_IncludesJsonSerializer()
    {
        var ids = CreateIds(StarterProjectRole.GodotClient, SerializerKind.Json, ClientEngineKind.Godot);

        Assert.Contains("Lakona.Rpc.Serializer.Json", ids);
    }

    [Fact]
    public void ConsoleMemoryPack_DoesNotRepeatSharedSerializerDependencies()
    {
        var ids = CreateIds(StarterProjectRole.ConsoleClient, SerializerKind.MemoryPack, ClientEngineKind.Console);

        Assert.Contains("Lakona.Rpc.Core", ids);
        Assert.Contains("Lakona.Rpc.Client", ids);
        Assert.Contains("Lakona.Rpc.Transport.WebSocket", ids);
        Assert.Contains("Lakona.Rpc.Analyzers", ids);
        Assert.DoesNotContain("Lakona.Rpc.Serializer.MemoryPack", ids);
        Assert.DoesNotContain("MemoryPack", ids);
        Assert.DoesNotContain("MemoryPack.Core", ids);
    }

    [Fact]
    public void ConsoleJson_IncludesJsonSerializer()
    {
        var ids = CreateIds(StarterProjectRole.ConsoleClient, SerializerKind.Json, ClientEngineKind.Console);

        Assert.Contains("Lakona.Rpc.Serializer.Json", ids);
        Assert.Contains("Lakona.Rpc.Analyzers", ids);
    }

    [Fact]
    public void UnityMemoryPack_KeepsExplicitSerializerAndRuntimeDependencies()
    {
        var plan = CreatePlan(StarterProjectRole.UnityClient, SerializerKind.MemoryPack, ClientEngineKind.Tuanjie);
        var ids = plan.PackageReferences.Select(static reference => reference.Id).ToArray();

        Assert.Contains("Lakona.Rpc.Serializer.MemoryPack", ids);
        Assert.Contains("Lakona.Rpc.Analyzers", ids);
        Assert.Contains("MemoryPack", ids);
        Assert.Contains("MemoryPack.Core", ids);
        Assert.Contains("MemoryPack.Generator", ids);
        Assert.Contains("Microsoft.CodeAnalysis.CSharp", ids);
        Assert.Contains("System.IO.Pipelines", ids);
        Assert.Contains(plan.PackageReferences, static reference =>
            reference.Id == "Lakona.Rpc.Serializer.MemoryPack" && reference.ManuallyInstalled);
    }

    [Fact]
    public void UnityJson_KeepsExplicitSerializerAndRuntimeDependencies()
    {
        var plan = CreatePlan(StarterProjectRole.UnityClient, SerializerKind.Json);
        var ids = plan.PackageReferences.Select(static reference => reference.Id).ToArray();

        Assert.Contains("Lakona.Rpc.Serializer.Json", ids);
        Assert.Contains("Microsoft.Bcl.AsyncInterfaces", ids);
        Assert.Contains("System.Text.Json", ids);
        Assert.Contains("System.IO.Pipelines", ids);
        Assert.Contains(plan.PackageReferences, static reference =>
            reference.Id == "Lakona.Rpc.Serializer.Json" && reference.ManuallyInstalled);
    }

    [Fact]
    public void UnityKcp_IncludesTransportRuntimeDependencies()
    {
        var ids = CreatePlan(StarterProjectRole.UnityClient, SerializerKind.Json, transport: TransportKind.Kcp)
            .PackageReferences
            .Select(static reference => reference.Id)
            .ToArray();

        Assert.Contains("Lakona.Rpc.Transport.Kcp", ids);
        Assert.Contains("Kcp", ids);
        Assert.Contains("System.Memory", ids);
        Assert.Contains("System.Threading.Tasks.Extensions", ids);
    }

    [Fact]
    public void SharedMemoryPackGenerator_UsesAnalyzerMetadata()
    {
        var generator = CreatePlan(StarterProjectRole.Shared, SerializerKind.MemoryPack)
            .PackageReferences
            .Single(static reference => reference.Id == "MemoryPack.Generator");

        Assert.Equal("all", generator.PrivateAssets);
        Assert.Equal("runtime; build; native; contentfiles; analyzers; buildtransitive", generator.IncludeAssets);
    }

    [Fact]
    public void SdkGeneratedProjects_UsePrivateAnalyzerPackage()
    {
        AssertPrivateAnalyzerPackage(StarterProjectRole.Server, ClientEngineKind.Unity);
        AssertPrivateAnalyzerPackage(StarterProjectRole.GodotClient, ClientEngineKind.Godot);
        AssertPrivateAnalyzerPackage(StarterProjectRole.ConsoleClient, ClientEngineKind.Console);
    }

    private static void AssertPrivateAnalyzerPackage(StarterProjectRole role, ClientEngineKind clientEngine)
    {
        var analyzer = CreatePlan(role, SerializerKind.Json, clientEngine)
            .PackageReferences
            .Single(static reference => reference.Id == "Lakona.Rpc.Analyzers");

        Assert.Equal("all", analyzer.PrivateAssets);
        Assert.Equal("runtime; build; native; contentfiles; analyzers; buildtransitive", analyzer.IncludeAssets);
    }

    private static string[] CreateIds(
        StarterProjectRole role,
        SerializerKind serializer,
        ClientEngineKind clientEngine = ClientEngineKind.Unity,
        TransportKind transport = TransportKind.WebSocket) =>
        CreatePlan(role, serializer, clientEngine, transport)
            .PackageReferences
            .Select(static reference => reference.Id)
            .ToArray();

    private static StarterDependencyPlan CreatePlan(
        StarterProjectRole role,
        SerializerKind serializer,
        ClientEngineKind clientEngine = ClientEngineKind.Unity,
        TransportKind transport = TransportKind.WebSocket)
    {
        var context = new StarterTemplateContext(
            "Starter",
            "starter",
            clientEngine,
            transport,
            serializer,
            NuGetForUnitySourceKind.Embedded,
            Versions,
            new StarterPaths("Root", "Root/Shared", "Root/Server", "Root/Server/Server", "Root/Client"));

        return StarterDependencyPlanner.Create(context, role);
    }
}
