using Microsoft.Extensions.DependencyInjection;
using Server.App.Generated;
using Server.App.Hosting;
using Lakona.Game.Server.Diagnostics;
using Lakona.Game.Server.Features;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Hosting;

return await LakonaGameServer.RunAsync(args, server => server
    .AddServices((services, configuration) =>
    {
        services.AddAgarSampleServer(configuration);
        services.AddMessageRecording();
        services.AddLakonaGameRuntimeValidation();

        services.AddLakonaGame(configuration, _ => { });
    })
    .UseGeneratedHotfixServices());
