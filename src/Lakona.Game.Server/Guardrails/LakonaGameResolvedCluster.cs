namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedCluster(
    IReadOnlyDictionary<string, string> AdvertisedEndpoints);
