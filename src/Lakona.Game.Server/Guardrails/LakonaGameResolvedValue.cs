namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedValue<T>(
    T Value,
    LakonaGameValueSource Source,
    string? Path = null);
