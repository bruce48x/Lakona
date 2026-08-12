namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedManagement(
    LakonaGameResolvedValue<bool> AdminEnabled,
    LakonaGameResolvedValue<string> HttpHost,
    LakonaGameResolvedValue<bool> AdminRequireLoopback);
