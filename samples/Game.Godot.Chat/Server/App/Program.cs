using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Server.App.Generated;
using Server.App.Lifecycle;
using Lakona.Game.Server.Features;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Sessions;

return await LakonaGameServer.RunAsync(args, server => server
    .AddServices((services, configuration) =>
    {
        services.AddLakonaGame(configuration);
        services.AddLakonaGameServerSessionCleanup(options =>
        {
            options.DisconnectedSessionRetention = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<IGameSessionLifecycleHandler, ChatPresenceLifecycleHandler>();
    })
    .UseGeneratedHotfixServices());
