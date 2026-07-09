using Server.App.State.Contracts;
using Server.App.State.Contracts.Matchmaking;
using Server.App.State.Matchmaking;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Microsoft.Extensions.DependencyInjection;
using Server.Hotfix.State.Matchmaking;

namespace Server.Hotfix.Timers;

public sealed class MatchmakingTimerCallbacks
{
    public static async ValueTask TickAsync(TimerTick<MatchmakingTimerArgs> tick)
    {
        var actors = tick.Services.GetRequiredService<MatchmakingActors>();
        await actors
            .Local(new MatchmakingQueueId("default"))
            .PostAsync(
                MatchmakingBehavior.RunTickAsync,
                new MatchmakingTickRequest
            {
                ObservedAtUtc = tick.ObservedAtUtc.UtcDateTime
            },
                tick.CancellationToken)
            .ConfigureAwait(false);
    }
}
