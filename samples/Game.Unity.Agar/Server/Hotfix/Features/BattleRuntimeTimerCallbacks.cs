using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Rooms;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Server.Hotfix.State.Rooms;

namespace Server.Hotfix.Features;

public sealed class BattleRuntimeTimerCallbacks
{
    public static ValueTask TickAsync(TimerTick<BattleRuntimeTimerArgs> tick)
    {
        var actors = tick.Services.GetRequiredService<IActorRuntime>();
        var rooms = tick.Services.GetRequiredService<RoomActors>();
        var logger = tick.Services.GetRequiredService<ILogger<BattleRuntimeTimerCallbacks>>();
        foreach (var roomId in actors.GetActiveActorIds(typeof(RoomActor)))
        {
            var result = rooms
                .Local(new RoomId(roomId.Value))
                .TryRunTickAsync(new RoomTickRequest
                {
                    ObservedAtUtc = tick.ObservedAtUtc.UtcDateTime
                }, tick.CancellationToken);
            if (result != ActorTellResult.Accepted)
            {
                logger.LogDebug(
                    "Battle runtime room tick enqueue was not accepted for room {RoomId}: {ActorTellResult}.",
                    roomId.Value,
                    result);
            }
        }

        return default;
    }
}
