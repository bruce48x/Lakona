namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed class HotfixActorTick
{
    public DateTime ObservedAtUtc { get; init; }

    public TimeSpan Interval { get; init; }

    public long Sequence { get; init; }

    public long DispatchTableVersion { get; init; }
}
