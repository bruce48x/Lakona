using Server.App.State.Contracts;
using Server.App.State.Contracts.Matchmaking;
using Server.App.State.Matchmaking;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Microsoft.Extensions.DependencyInjection;
using Server.Hotfix.State.Matchmaking;

namespace Server.Hotfix.Timers;

public sealed class MatchmakingTimerCallbacks
{
    public static async ValueTask TickAsync(TimerTick<MatchmakingTimerArgs> tick)
    {
        var runtime = tick.Services.GetRequiredService<IActorRuntime>();
        await runtime.TellAsync<MatchmakingActor>(
            ActorId.From(tick.Args.OwnerActorId),
            (actor, cancellationToken) => MatchmakingBehavior.RunTickAsync(
                actor,
                new MatchmakingTickRequest
            {
                ObservedAtUtc = tick.ObservedAtUtc.UtcDateTime
            },
                cancellationToken),
            tick.CancellationToken)
            .ConfigureAwait(false);
    }
}
