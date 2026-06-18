using Agar.Sample.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Server.App.Hosting;
using Server.App.Realtime;
using Server.App.Services;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Features;
using Lakona.Rpc.Server;

namespace Server.App.Features;

public sealed class BattleRuntimeFeature : LakonaGameFeature
{
    public override void ConfigureServices(LakonaGameFeatureContext context)
    {
        context.Services.AddAgarSampleState();
        context.Services.TryAddSingleton<SessionDirectory>();
        var runtimeEndpoint = context.Endpoints.RequireTransport("kcp");
        context.Services.TryAddSingleton<LakonaGameEndpointOptions>(_ => runtimeEndpoint);
        context.Services.TryAddSingleton(_ => new GatewayNodeIdentity(
            GatewayEndpointDescriptorFactory.FromConfiguredEndpoint(context.Configuration, runtimeEndpoint)));
        context.Services.TryAddSingleton<RoomRuntimeHost>();
        context.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IRpcSessionLifecycleObserver, PlayerSessionLifecycleObserver>());
    }
}
