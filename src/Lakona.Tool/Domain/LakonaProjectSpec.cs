namespace Lakona.Tool.Domain;

internal sealed record LakonaProjectSpec(
    string Name,
    ProjectLayout Layout,
    ClientEngine ClientEngine,
    TransportKind Transport,
    SerializerKind Serializer,
    PersistenceKind Persistence,
    NuGetForUnitySource NuGetForUnitySource,
    DeploymentProfile DeploymentProfile,
    IReadOnlyList<ProjectFeature> Features);
