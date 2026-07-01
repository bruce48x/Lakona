using Agar.Sample.State.Contracts.Matchmaking;
using Agar.Sample.State.Matchmaking;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Microsoft.Extensions.DependencyInjection;
using Server.Hotfix.State.Matchmaking;

namespace Server.Hotfix.Features;

public sealed class MatchmakingTimerCallbacks
{
    public static ValueTask TickAsync(TimerTick<MatchmakingTimerArgs> tick)
    {
        var actors = tick.Services.GetRequiredService<IActorRuntime>();
        actors.TryTell<MatchmakingActor>(
            ActorId.From("default"),
            (actor, cancellationToken) => actor.RunTickAsync(new MatchmakingTickRequest
            {
                ObservedAtUtc = tick.ObservedAtUtc.UtcDateTime
            }, cancellationToken),
            tick.CancellationToken);
        return default;
    }
}
