namespace Lakona.Game.Server.Hotfix.Abstractions.Timers;

public readonly record struct TimerTick(
    TimerId TimerId,
    DateTimeOffset ScheduledAtUtc,
    DateTimeOffset FiredAtUtc);
