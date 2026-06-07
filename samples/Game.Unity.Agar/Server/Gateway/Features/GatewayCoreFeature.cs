using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Diagnostics;
using Lakona.Game.Server.Features;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Sessions;

namespace Gateway.Features;

public sealed class GatewayCoreFeature : LakonaGameFeature
{
    public override void ConfigureServices(LakonaGameFeatureContext context)
    {
        var services = context.Services;

        services.AddLakonaGameServerActors(options =>
        {
            options.MailboxCapacity = 4096;
            options.CallTimeout = TimeSpan.FromSeconds(5);
            options.SlowMessageThreshold = TimeSpan.FromSeconds(1);
        });
        services.AddLakonaGameServer();
        services.AddLakonaGameServerSessionCleanup(options =>
        {
            options.Interval = TimeSpan.FromSeconds(30);
            options.DisconnectedEndpointRetention = TimeSpan.FromMinutes(2);
        });
        services.AddMessageRecording();
        services.AddLakonaGameRuntimeValidation();
        services.AddLakonaGameServerGateway();
    }
}
