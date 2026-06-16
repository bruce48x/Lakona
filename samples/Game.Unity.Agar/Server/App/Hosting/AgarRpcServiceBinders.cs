using Microsoft.Extensions.DependencyInjection;
using Server.App.Generated;
using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.App.Hosting;

[LakonaRpcService("login")]
internal sealed class LoginRpcServiceBinder : LakonaRpcServiceBinder
{
    public override void Bind(LakonaGameServerRpcContext context)
    {
        PlayerRpcServiceBinding.Bind(context);
    }
}

[LakonaRpcService("player")]
internal sealed class PlayerRpcServiceBinder : LakonaRpcServiceBinder
{
    public override void Bind(LakonaGameServerRpcContext context)
    {
        PlayerRpcServiceBinding.Bind(context);
    }
}

[LakonaRpcService("battle")]
internal sealed class BattleRpcServiceBinder : LakonaRpcServiceBinder
{
    public override void Bind(LakonaGameServerRpcContext context)
    {
        PlayerRpcServiceBinding.Bind(context);
    }
}

internal static class PlayerRpcServiceBinding
{
    public static void Bind(LakonaGameServerRpcContext context)
    {
        PlayerServiceBinder.BindFactory(
            context.Builder.ServiceRegistry,
            session => new PlayerServiceProxy(
                context.Services.GetRequiredService<IHotfixServiceInvoker>(),
                context.Services,
                context.Services.GetRequiredService<IActorRuntime>(),
                context.Services.GetRequiredService<ILakonaGameServer>(),
                new PlayerCallbackProxy(session),
                session.ContextId));
    }
}
