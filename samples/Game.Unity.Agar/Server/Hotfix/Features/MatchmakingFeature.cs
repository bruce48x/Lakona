using Agar.Sample.State.Matchmaking;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Server.Hotfix.State.Matchmaking;

namespace Server.Hotfix.Features;

[HotfixFeature("matchmaking")]
public sealed class MatchmakingFeature : HotfixGameFeature
{
    public static void Configure(HotfixFeatureContext context)
    {
        context.EnsureLocalActor<MatchmakingActor>("default");
    }

    public static async ValueTask StartAsync(HotfixFeatureStartCall call)
    {
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
        if (call.State.Items.TryGetValue(FeatureTimerKeys.MatchmakingTimerId, out var value) &&
            value is TimerId timerId &&
            timerId.IsValid)
        {
            await LakonaTimer.DestroyTimerAsync(timerId, CancellationToken.None).ConfigureAwait(false);
        }

        call.State.Items.Remove(FeatureTimerKeys.MatchmakingTimerId);
    }
}
