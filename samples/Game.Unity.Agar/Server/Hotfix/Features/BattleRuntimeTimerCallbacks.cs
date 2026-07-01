using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Rooms;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Microsoft.Extensions.DependencyInjection;
using Server.Hotfix.State.Rooms;

namespace Server.Hotfix.Features;

public sealed class BattleRuntimeTimerCallbacks
{
    public static ValueTask TickAsync(TimerTick<BattleRuntimeTimerArgs> tick)
    {
        var actors = tick.Services.GetRequiredService<IActorRuntime>();
        foreach (var roomId in actors.GetActiveActorIds(typeof(RoomActor)))
        {
            actors.TryTell<RoomActor>(
                roomId,
                (actor, cancellationToken) => actor.RunTickAsync(new RoomTickRequest
                {
                    ObservedAtUtc = tick.ObservedAtUtc.UtcDateTime
                }, cancellationToken),
                tick.CancellationToken);
        }

        return default;
    }
}
