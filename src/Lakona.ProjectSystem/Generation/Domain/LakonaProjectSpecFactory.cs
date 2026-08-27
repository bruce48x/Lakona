using Lakona.ProjectSystem;

namespace Lakona.ProjectSystem.Generation.Domain;

internal sealed class ProjectSpecFactory
{
    public LakonaProjectSpec Create(LakonaProjectCreationRequest request)
    {
        var projectName = string.IsNullOrWhiteSpace(request.ProjectName) ? "MyGame" : request.ProjectName;
        var clientEngine = Map(request.ClientEngine);
        var layout = ProjectLayout.Create(projectName, request.OutputPath);
        var nuGetForUnitySource = ClientEnginePolicy.GetEffectiveNuGetForUnitySource(
            clientEngine,
            Map(request.NuGetForUnitySource));
        var clientEngineVersion = ClientEngineVersionPolicy.Resolve(
            clientEngine,
            request.ClientEngineVersion is { } version ? Map(version) : null);

        return new LakonaProjectSpec(
            projectName,
            layout,
            clientEngine,
            clientEngineVersion,
            Map(request.Transport),
            Map(request.Serializer),
            nuGetForUnitySource,
            Map(request.DeploymentProfile),
            Map(request.MembershipProvider),
            ProjectCapabilityCatalog.DefaultCapabilities);
    }

    private static ClientEngine Map(LakonaClientEngine value) => value switch
    {
        LakonaClientEngine.Unity => ClientEngine.Unity,
        LakonaClientEngine.Tuanjie => ClientEngine.Tuanjie,
        LakonaClientEngine.Godot => ClientEngine.Godot,
        LakonaClientEngine.Console => ClientEngine.Console,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static ClientEngineVersion Map(LakonaClientEngineVersion value) => value switch
    {
        LakonaClientEngineVersion.Unity2022 => ClientEngineVersion.Unity2022,
        LakonaClientEngineVersion.Unity60 => ClientEngineVersion.Unity60,
        LakonaClientEngineVersion.Unity63 => ClientEngineVersion.Unity63,
        LakonaClientEngineVersion.Tuanjie167 => ClientEngineVersion.Tuanjie167,
        LakonaClientEngineVersion.Godot46 => ClientEngineVersion.Godot46,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static TransportKind Map(LakonaTransport value) => value switch
    {
        LakonaTransport.Tcp => TransportKind.Tcp,
        LakonaTransport.WebSocket => TransportKind.WebSocket,
        LakonaTransport.Kcp => TransportKind.Kcp,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static SerializerKind Map(LakonaSerializer value) => value switch
    {
        LakonaSerializer.Json => SerializerKind.Json,
        LakonaSerializer.MemoryPack => SerializerKind.MemoryPack,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static NuGetForUnitySource Map(LakonaNuGetForUnitySource value) => value switch
    {
        LakonaNuGetForUnitySource.Embedded => NuGetForUnitySource.Embedded,
        LakonaNuGetForUnitySource.OpenUpm => NuGetForUnitySource.OpenUpm,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static DeploymentProfile Map(LakonaDeploymentProfile value) => value switch
    {
        LakonaDeploymentProfile.None => DeploymentProfile.None,
        LakonaDeploymentProfile.Compose => DeploymentProfile.Compose,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static MembershipProviderKind Map(LakonaMembershipProvider value) => value switch
    {
        LakonaMembershipProvider.Memory => MembershipProviderKind.Memory,
        LakonaMembershipProvider.Postgres => MembershipProviderKind.Postgres,
        LakonaMembershipProvider.Redis => MembershipProviderKind.Redis,
        LakonaMembershipProvider.MySql => MembershipProviderKind.MySql,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };
}
