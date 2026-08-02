using Game.Unity.MMO.Server.App.World;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Server.App.Generated;

namespace Game.Unity.MMO.Server.Hotfix.World;

[HotfixTimer]
public sealed partial class ZoneTimerCallbacks
{
    private readonly ActorAccess _actors;
    public ZoneTimerCallbacks(ActorAccess actors) => _actors = actors;

    public ValueTask TickAsync(TimerTick<ZoneTimerArgs> tick) => _actors
        .Startup<ZoneActor>(new ZoneId(tick.Args.ZoneId))
        .PostAsync(static behavior => behavior.TickAsync,
            new ZoneTickRequest { ObservedAtUtc = tick.ObservedAtUtc.UtcDateTime },
            tick.CancellationToken);
}
