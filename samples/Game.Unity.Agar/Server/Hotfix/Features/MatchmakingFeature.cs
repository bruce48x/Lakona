using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Matchmaking;
using Agar.Sample.State.Matchmaking;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Server.Hotfix.Services;
using Server.Hotfix.State.Matchmaking;

namespace Server.Hotfix.Features;

[HotfixFeature("matchmaking")]
public sealed class MatchmakingFeature : HotfixGameFeature
{
    private const string MatchmakingActorId = "default";
    private const string MatchmakingActorStateKey = "MatchmakingActorId";

    public static void Configure(HotfixFeatureContext context)
    {
        context.Services.TryAddSingleton<MatchmakingNotifier>();
    }

    public static async ValueTask StartAsync(HotfixFeatureStartCall call)
    {
        await call.Services
            .GetRequiredService<ActorHosting>()
            .CreateAsync<MatchmakingActor>(ActorId.From(MatchmakingActorId), call.CancellationToken)
            .ConfigureAwait(false);
        call.State.Items[MatchmakingActorStateKey] = MatchmakingActorId;

        await call.Services
            .GetRequiredService<MatchmakingActors>()
            .Local(new MatchmakingQueueId(MatchmakingActorId))
            .StartTimerAsync(new MatchmakingTimerStartRequest(), call.CancellationToken)
            .ConfigureAwait(false);
    }

    public static async ValueTask StopAsync(HotfixFeatureStopCall call)
    {
        if (call.State.Items.TryGetValue(MatchmakingActorStateKey, out var actorValue) &&
            actorValue is string actorId &&
            !string.IsNullOrWhiteSpace(actorId))
        {
            await call.Services
                .GetRequiredService<MatchmakingActors>()
                .Local(new MatchmakingQueueId(actorId))
                .StopTimerAsync(new MatchmakingTimerStopRequest(), CancellationToken.None)
                .ConfigureAwait(false);

            await call.Services
                .GetRequiredService<ActorHosting>()
                .DestroyAsync<MatchmakingActor>(ActorId.From(actorId), CancellationToken.None)
                .ConfigureAwait(false);
        }

        call.State.Items.Remove(MatchmakingActorStateKey);
    }
}
