namespace Lakona.Tool.Domain;

internal enum ProjectCapability
{
    ClusterLocal,
    Hotfix,
    ReliablePush,
    LoginSlice,
    GameSlice
}

internal static class ProjectCapabilityCatalog
{
    public static readonly IReadOnlyList<ProjectCapability> DefaultCapabilities =
    [
        ProjectCapability.ClusterLocal,
        ProjectCapability.Hotfix,
        ProjectCapability.ReliablePush,
        ProjectCapability.LoginSlice,
        ProjectCapability.GameSlice
    ];
}
