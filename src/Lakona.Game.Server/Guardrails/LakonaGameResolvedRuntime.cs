namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedRuntime(
    LakonaGameResolvedValue<string> NodeId,
    IReadOnlyList<LakonaGameResolvedEndpoint> Endpoints,
    LakonaGameResolvedCluster Cluster,
    LakonaGameResolvedClusterEndpoint? ClusterEndpoint,
    LakonaGameResolvedHotfix Hotfix,
    LakonaGameResolvedReliablePush ReliablePush,
    LakonaGameResolvedHeartbeat Heartbeat,
    LakonaGameResolvedManagement Management,
    IReadOnlyList<LakonaGameResolvedValue<string>>? ActorHosts = null);
