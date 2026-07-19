using Server.App.State.Contracts;
using Server.App.State.Contracts.Matchmaking;
using Server.App.State.Contracts.Timers;
using Server.App.State.Matchmaking;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Server.Hotfix.State.Matchmaking;

namespace Server.Hotfix.Timers;

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
            .Local<MatchmakingActor>(new MatchmakingQueueId(tick.Args.OwnerActorId))
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
