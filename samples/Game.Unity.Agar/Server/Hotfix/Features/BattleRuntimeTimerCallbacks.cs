using Agar.Sample.State.Contracts;
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
        var rooms = tick.Services.GetRequiredService<RoomActors>();
        foreach (var roomId in actors.GetActiveActorIds(typeof(RoomActor)))
        {
            rooms
                .Local(new RoomId(roomId.Value))
                .TryRunTickAsync(new RoomTickRequest
                {
                    ObservedAtUtc = tick.ObservedAtUtc.UtcDateTime
                }, tick.CancellationToken);
        }

        return default;
    }
}
