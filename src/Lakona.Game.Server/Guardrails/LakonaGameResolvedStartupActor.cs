namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedStartupActor(
    LakonaGameResolvedValue<string> Name,
    IReadOnlyDictionary<string, string>? Options = null);
