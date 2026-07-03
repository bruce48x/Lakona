using Agar.Sample.State.Leaderboard;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Server.Hotfix;

namespace Server.Hotfix.Features;

[HotfixFeature("leaderboard")]
public sealed class LeaderboardFeature : HotfixGameFeature
{
    private const string LeaderboardActorStateKey = "LeaderboardActorId";

    public static void Configure(HotfixFeatureContext context)
    {
    }

    public static async ValueTask StartAsync(HotfixFeatureStartCall call)
    {
        await call.Services
            .GetRequiredService<ActorHosting>()
            .CreateAsync<LeaderboardActor>(ActorId.From(AgarHotfixIds.GlobalLeaderboardActorId), call.CancellationToken)
            .ConfigureAwait(false);
        call.State.Items[LeaderboardActorStateKey] = AgarHotfixIds.GlobalLeaderboardActorId;
    }

    public static async ValueTask StopAsync(HotfixFeatureStopCall call)
    {
        if (call.State.Items.TryGetValue(LeaderboardActorStateKey, out var actorValue) &&
            actorValue is string actorId &&
            !string.IsNullOrWhiteSpace(actorId))
        {
            await call.Services
                .GetRequiredService<ActorHosting>()
                .DestroyAsync<LeaderboardActor>(ActorId.From(actorId), CancellationToken.None)
                .ConfigureAwait(false);
        }

        call.State.Items.Remove(LeaderboardActorStateKey);
    }
}
