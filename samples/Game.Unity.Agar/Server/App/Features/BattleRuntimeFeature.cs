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
        context.Services.TryAddSingleton<LakonaGameEndpointOptions>(_ => context.Endpoints.RequireTransport("kcp"));
        context.Services.TryAddSingleton(_ => new GatewayNodeIdentity(
            context.Configuration,
            context.Endpoints.RequireTransport("kcp")));
        context.Services.TryAddSingleton<RoomRuntimeHost>();
        context.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IRpcSessionLifecycleObserver, PlayerSessionLifecycleObserver>());
    }
}
