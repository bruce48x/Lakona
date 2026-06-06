namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedEndpoint(
    LakonaGameResolvedValue<string> Transport,
    LakonaGameResolvedValue<string> Host,
    LakonaGameResolvedValue<int> Port,
    LakonaGameResolvedValue<string> Path,
    LakonaGameResolvedValue<string> AdvertisedHost,
    LakonaGameResolvedValue<string> AdvertisedEndpoint);
