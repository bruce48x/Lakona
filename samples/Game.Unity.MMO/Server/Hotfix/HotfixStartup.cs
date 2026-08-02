using Game.Unity.MMO.Server.App.World;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Game.Unity.MMO.Server.Hotfix;

[HotfixStartup]
public static class HotfixStartup
{
    [HotfixConfigureActors]
    public static void ConfigureActors(ActorHostBuilder actors)
    {
        actors.RegisterStartup<ZoneActor, ZoneId>(static context => context.Candidates[0]);
    }
}
