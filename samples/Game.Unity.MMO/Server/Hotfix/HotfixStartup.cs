using Game.Unity.MMO.Server.App.World;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Rpc.Server;
using Microsoft.Extensions.DependencyInjection;
using Shared.Interfaces;

namespace Game.Unity.MMO.Server.Hotfix;

[HotfixStartup]
public static class HotfixStartup
{
    [HotfixConfigureServices]
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<RpcServiceExceptionHandler>(static context =>
            context.ResponseType == typeof(EnterWorldReply)
                ? new EnterWorldReply { Code = WorldResultCodes.ServerInternalError, Message = "Internal server error." }
                : null);
    }

    [HotfixConfigureActors]
    public static void ConfigureActors(ActorHostBuilder actors)
    {
        actors.RegisterStartup<ZoneActor, ZoneId>(static context => context.Candidates[0]);
    }
}
