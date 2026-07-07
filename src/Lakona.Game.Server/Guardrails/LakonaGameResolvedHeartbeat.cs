namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedHeartbeat(
    LakonaGameResolvedValue<TimeSpan> Interval,
    LakonaGameResolvedValue<TimeSpan> Timeout);
