namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedClusterEndpoint(
    LakonaGameResolvedValue<string> Endpoint,
    LakonaGameResolvedValue<string> Serializer,
    IReadOnlyList<string> Seeds);
