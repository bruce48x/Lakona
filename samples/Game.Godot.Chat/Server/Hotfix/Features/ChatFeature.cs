using Server.App.Chat;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Hotfix.Features
{
    [HotfixFeature("chat")]
    public sealed class ChatFeature : HotfixGameFeature
    {
        public static void Configure(HotfixFeatureContext context)
        {
        }

        public static async ValueTask StartAsync(HotfixFeatureStartCall call)
        {
            await call.Services
                .GetRequiredService<ActorHosting>()
                .EnsureAsync<ChatRoomActor>(ActorId.From(ChatRoomIds.Global), call.CancellationToken)
                .ConfigureAwait(false);
            call.State.Items[nameof(ChatRoomIds.Global)] = ChatRoomIds.Global;
        }

        public static async ValueTask StopAsync(HotfixFeatureStopCall call)
        {
            if (call.State.Items.TryGetValue(nameof(ChatRoomIds.Global), out var value) &&
                value is string actorId &&
                !string.IsNullOrWhiteSpace(actorId))
            {
                await call.Services
                    .GetRequiredService<ActorHosting>()
                    .DestroyAsync<ChatRoomActor>(ActorId.From(actorId), CancellationToken.None)
                    .ConfigureAwait(false);
            }

            call.State.Items.Remove(nameof(ChatRoomIds.Global));
        }
    }
}
