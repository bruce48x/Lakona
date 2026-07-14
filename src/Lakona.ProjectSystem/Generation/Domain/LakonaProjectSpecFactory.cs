using Lakona.ProjectSystem;

namespace Lakona.Tool.Domain;

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
            Map(request.Persistence),
            nuGetForUnitySource,
            Map(request.DeploymentProfile),
            ProjectCapabilityCatalog.DefaultCapabilities);
    }

    private static ClientEngine Map(LakonaClientEngine value) => (ClientEngine)value;
    private static ClientEngineVersion Map(LakonaClientEngineVersion value) => (ClientEngineVersion)value;
    private static TransportKind Map(LakonaTransport value) => (TransportKind)value;
    private static SerializerKind Map(LakonaSerializer value) => (SerializerKind)value;
    private static PersistenceKind Map(LakonaPersistence value) => (PersistenceKind)value;
    private static NuGetForUnitySource Map(LakonaNuGetForUnitySource value) => (NuGetForUnitySource)value;
    private static DeploymentProfile Map(LakonaDeploymentProfile value) => (DeploymentProfile)value;
}
