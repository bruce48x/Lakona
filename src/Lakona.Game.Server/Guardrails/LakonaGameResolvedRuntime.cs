namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedRuntime(
    LakonaGameResolvedValue<string> NodeId,
    IReadOnlyList<LakonaGameResolvedEndpoint> Endpoints,
    LakonaGameResolvedCluster Cluster,
    LakonaGameResolvedClusterEndpoint? ClusterEndpoint,
    LakonaGameResolvedFeature Feature,
    LakonaGameResolvedHotfix Hotfix,
    LakonaGameResolvedReliablePush ReliablePush,
    LakonaGameRuntimeProfile Profile);
