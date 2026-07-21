using Lakona.Game.Server.Hosting;
using Lakona.Game.Cluster;
using Microsoft.Extensions.DependencyInjection;
using Server.App;
using Server.App.State.Contracts;

return await LakonaGameServer.RunAsync(args, builder => builder.AddServices(services =>
{
    services.AddSingleton<AgarBattleEndpointAdvertisement>();
    services.AddSingleton<INodeAdvertisementProvider>(provider =>
        provider.GetRequiredService<AgarBattleEndpointAdvertisement>());
    services.AddSingleton<INodeAdvertisementResolver<GatewayEndpointDescriptor>>(provider =>
        provider.GetRequiredService<AgarBattleEndpointAdvertisement>());
}));
