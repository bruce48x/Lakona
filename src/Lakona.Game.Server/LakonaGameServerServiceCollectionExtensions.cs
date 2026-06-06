using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;

namespace Lakona.Game.Server;

public static class LakonaGameServerServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameServer(this IServiceCollection services)
    {
        services.AddLakonaGameServerActors();
        services.AddLakonaGameServerSessions();
        services.AddLakonaGameServerReliablePush();
        services.TryAddSingleton<ILakonaGameServer, LakonaGameServer>();
        return services;
    }
}
