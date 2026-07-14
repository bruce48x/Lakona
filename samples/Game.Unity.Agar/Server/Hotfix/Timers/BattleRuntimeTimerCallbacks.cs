using Server.App.State.Contracts;
using Server.App.State.Contracts.Rooms;
using Server.App.State.Rooms;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Server.Hotfix.State.Rooms;

namespace Server.Hotfix.Timers;

public sealed class BattleRuntimeTimerCallbacks
{
    public static async ValueTask TickAsync(TimerTick<BattleRuntimeTimerArgs> tick)
    {
        var actors = tick.Services.GetRequiredService<ActorAccess>();
        var logger = tick.Services.GetRequiredService<ILogger<BattleRuntimeTimerCallbacks>>();
        if (string.IsNullOrWhiteSpace(tick.Args.RoomId))
        {
            logger.LogDebug("Battle runtime timer tick skipped because no room id was provided.");
            return;
        }

        await actors
            .Local<RoomActor>(new RoomId(tick.Args.RoomId))
            .PostAsync(
                RoomBehavior.RunTickAsync,
                new RoomTickRequest
            {
                ObservedAtUtc = tick.ObservedAtUtc.UtcDateTime
            },
                tick.CancellationToken)
            .ConfigureAwait(false);
    }
}
