using Server.App.Chat;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Server.Hotfix.Chat;

namespace Server.Hotfix
{
    [HotfixStartup]
    public static class HotfixStartup
    {
        [HotfixConfigureServices]
        public static void ConfigureServices(IServiceCollection services)
        {
            services.TryAddSingleton<ChatNotifier>();
        }

        [HotfixConfigureActors]
        public static void ConfigureActors(ActorHostBuilder actors)
        {
            actors.RegisterStartup<ChatRoomActor, string>(
                static context => context.Candidates[0]);
        }
    }
}
