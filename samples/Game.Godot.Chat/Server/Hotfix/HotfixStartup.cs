using Server.App.Chat;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix
{
    public static class HotfixStartup
    {
        public static void ConfigureActors(ActorHostBuilder actors)
        {
            actors.RegisterStartup(
                "chat-room",
                static _ => ActorStartupPlan.Create<ChatRoomActor>(ActorId.From(ChatRoomIds.Global)));
        }
    }
}
