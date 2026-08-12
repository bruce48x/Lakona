using Server.App.Routing;
using Server.App.Matchmaking;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
namespace Server.Hotfix.Matchmaking;

[HotfixTimer]
public sealed partial class MatchmakingTimerCallbacks
{
    private readonly ActorAccess _actors;

    public MatchmakingTimerCallbacks(ActorAccess actors)
    {
        _actors = actors;
    }

    public async ValueTask TickAsync(TimerTick<MatchmakingTimerArgs> tick)
    {
        await _actors
            .LocalExact<MatchmakingActor>(ActorId.From(tick.Args.OwnerActorId))
            .PostAsync(
                static behavior => behavior.RunTickAsync,
                new MatchmakingTickRequest
                {
                    ObservedAtUtc = tick.ObservedAtUtc.UtcDateTime
                },
                tick.CancellationToken)
            .ConfigureAwait(false);
    }
}
