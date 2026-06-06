namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedClusterEndpoint(
    LakonaGameResolvedValue<string> Endpoint,
    IReadOnlyList<string> Seeds);
