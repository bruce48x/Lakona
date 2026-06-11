namespace Lakona.Tool.Domain;

internal enum ProjectFeature
{
    ClusterLocal,
    Hotfix,
    ReliablePush,
    LoginSlice,
    ChatSlice
}

internal static class ProjectFeatureCatalog
{
    public static readonly IReadOnlyList<ProjectFeature> DefaultFeatures =
    [
        ProjectFeature.ClusterLocal,
        ProjectFeature.Hotfix,
        ProjectFeature.ReliablePush,
        ProjectFeature.LoginSlice,
        ProjectFeature.ChatSlice
    ];
}
