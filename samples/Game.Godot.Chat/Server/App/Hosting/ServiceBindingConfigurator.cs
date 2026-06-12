using System;
using Microsoft.Extensions.DependencyInjection;
using Server.App.Chat;
using Server.App.Generated;
using Shared.Contracts.Chat;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Rpc.Server;

namespace Server.App.Hosting;

internal static class ServiceBindingConfigurator
{
    public static void Bind(RpcServiceRegistry registry, IServiceProvider services)
    {
        LoginServiceBinder.BindFactory(
            registry,
            session =>
            {
                        services.GetRequiredService<ChatConnectionLifecycle>().Track(session);
                        return new LoginServiceProxy(
                            services.GetRequiredService<IHotfixServiceInvoker>(),
                            services.GetRequiredService<IActorRuntime>(),
                            new LoginCallbackProxy(session),
                            session.ContextId);
                    });

        ChatServiceBinder.BindFactory(
            registry,
            session =>
            {
                        services.GetRequiredService<ChatConnectionLifecycle>().Track(session);
                        return new ChatServiceProxy(
                            services.GetRequiredService<IHotfixServiceInvoker>(),
                            services.GetRequiredService<IActorRuntime>(),
                            new ChatCallbackProxy(session),
                            session.ContextId);
                    });
    }
}
