namespace Lakona.Tool.Domain;

internal sealed record LakonaProjectSpec(
    string Name,
    ProjectLayout Layout,
    ClientEngine ClientEngine,
    ClientEngineVersion? ClientEngineVersion,
    TransportKind Transport,
    SerializerKind Serializer,
    PersistenceKind Persistence,
    NuGetForUnitySource NuGetForUnitySource,
    DeploymentProfile DeploymentProfile,
    IReadOnlyList<ProjectCapability> Capabilities);
