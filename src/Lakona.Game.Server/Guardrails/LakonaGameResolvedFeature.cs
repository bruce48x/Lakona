namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedFeature(
    IReadOnlyList<string>? Configured,
    IReadOnlyList<string> Active,
    IReadOnlyList<string> StartupOrder);
