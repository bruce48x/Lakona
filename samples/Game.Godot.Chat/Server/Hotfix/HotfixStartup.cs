using Server.App.Chat;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix
{
    [HotfixStartup]
    public static class HotfixStartup
    {
        [HotfixConfigureActors]
        public static void ConfigureActors(ActorHostBuilder actors)
        {
            actors.RegisterStartup<ChatRoomActor, string>(
                static context => context.Candidates[0]);
        }
    }
}
