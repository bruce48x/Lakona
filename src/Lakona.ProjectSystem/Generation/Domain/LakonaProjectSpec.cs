namespace Lakona.ProjectSystem.Generation.Domain;

internal sealed record LakonaProjectSpec(
    string Name,
    ProjectLayout Layout,
    ClientEngine ClientEngine,
    ClientEngineVersion? ClientEngineVersion,
    TransportKind Transport,
    SerializerKind Serializer,
    NuGetForUnitySource NuGetForUnitySource,
    DeploymentProfile DeploymentProfile,
    MembershipProviderKind MembershipProvider,
    string? ClientEditorPath = null,
    string? ClientEditorVersion = null,
    string? ClientEditorRevision = null);
