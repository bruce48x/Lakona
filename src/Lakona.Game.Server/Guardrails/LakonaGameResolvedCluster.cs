namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedCluster(
    IReadOnlyList<LakonaGameResolvedClusterService> Services,
    IReadOnlyDictionary<string, string> AdvertisedEndpoints);

public sealed record LakonaGameResolvedClusterService(
    string Kind,
    string Name);
