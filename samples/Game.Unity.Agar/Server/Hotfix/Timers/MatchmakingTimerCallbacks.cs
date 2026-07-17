using Server.App.State.Contracts;
using Server.App.State.Contracts.Matchmaking;
using Server.App.State.Contracts.Timers;
using Server.App.State.Matchmaking;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Microsoft.Extensions.DependencyInjection;
using Server.Hotfix.State.Matchmaking;

namespace Server.Hotfix.Timers;

[HotfixTimer]
public sealed partial class MatchmakingTimerCallbacks
{
    public async ValueTask TickAsync(TimerTick<MatchmakingTimerArgs> tick)
    {
        var actors = tick.Services.GetRequiredService<ActorAccess>();
        await actors
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
