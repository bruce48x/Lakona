using Agar.Sample.State.Matchmaking;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Microsoft.Extensions.DependencyInjection;
using Server.Hotfix.State.Matchmaking;

namespace Server.Hotfix.Features;

[HotfixFeature("matchmaking")]
public sealed class MatchmakingFeature : HotfixGameFeature
{
    private const string MatchmakingActorId = "default";
    private const string MatchmakingActorStateKey = "MatchmakingActorId";

    public static void Configure(HotfixFeatureContext context)
    {
    }

    public static async ValueTask StartAsync(HotfixFeatureStartCall call)
    {
        await call.Services
            .GetRequiredService<ActorHosting>()
            .CreateAsync<MatchmakingActor>(ActorId.From(MatchmakingActorId), call.CancellationToken)
            .ConfigureAwait(false);
        call.State.Items[MatchmakingActorStateKey] = MatchmakingActorId;

        var timerId = await LakonaTimer
            .CreatePeriodicTimerAsync<MatchmakingTimerCallbacks, MatchmakingTimerArgs>(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                nameof(MatchmakingTimerCallbacks.TickAsync),
                new MatchmakingTimerArgs(),
                call.CancellationToken)
            .ConfigureAwait(false);
        call.State.Items[FeatureTimerKeys.MatchmakingTimerId] = timerId;
    }

    public static async ValueTask StopAsync(HotfixFeatureStopCall call)
    {
        if (call.State.Items.TryGetValue(MatchmakingActorStateKey, out var actorValue) &&
            actorValue is string actorId &&
            !string.IsNullOrWhiteSpace(actorId))
        {
            await call.Services
                .GetRequiredService<ActorHosting>()
                .DestroyAsync<MatchmakingActor>(ActorId.From(actorId), CancellationToken.None)
                .ConfigureAwait(false);
        }

        call.State.Items.Remove(MatchmakingActorStateKey);

        if (call.State.Items.TryGetValue(FeatureTimerKeys.MatchmakingTimerId, out var value) &&
            value is TimerId timerId &&
            timerId.IsValid)
        {
            await LakonaTimer.DestroyTimerAsync(timerId, CancellationToken.None).ConfigureAwait(false);
        }

        call.State.Items.Remove(FeatureTimerKeys.MatchmakingTimerId);
    }
}
