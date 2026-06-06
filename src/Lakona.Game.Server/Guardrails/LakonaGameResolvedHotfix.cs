namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedHotfix(
    LakonaGameResolvedValue<string> AssemblyPath,
    LakonaGameResolvedValue<string> AssemblyFileName);
