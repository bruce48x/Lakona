using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Matchmaking;
using Agar.Sample.State.Matchmaking;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Microsoft.Extensions.DependencyInjection;
using Server.Hotfix.State.Matchmaking;

namespace Server.Hotfix.Features;

public sealed class MatchmakingTimerCallbacks
{
    public static async ValueTask TickAsync(TimerTick<MatchmakingTimerArgs> tick)
    {
        var actors = tick.Services.GetRequiredService<MatchmakingActors>();
        await actors
            .Local(new MatchmakingQueueId("default"))
            .RunTickAsync(new MatchmakingTickRequest
            {
                ObservedAtUtc = tick.ObservedAtUtc.UtcDateTime
            }, tick.CancellationToken)
            .ConfigureAwait(false);
    }
}
