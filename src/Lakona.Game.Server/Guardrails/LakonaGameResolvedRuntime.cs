namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedRuntime(
    LakonaGameResolvedValue<string> NodeId,
    IReadOnlyList<LakonaGameResolvedEndpoint> Endpoints,
    LakonaGameResolvedCluster Cluster,
    LakonaGameResolvedClusterEndpoint? ClusterEndpoint,
    LakonaGameResolvedHotfix Hotfix,
    LakonaGameResolvedReliablePush ReliablePush,
    LakonaGameResolvedHeartbeat Heartbeat,
    LakonaGameResolvedObservability Observability,
    IReadOnlyList<LakonaGameResolvedValue<string>>? ActorHosts = null);
