using System;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Server.App.Chat;
using Server.App.Generated;
using Shared.Contracts.Chat;
using Lakona.Rpc.Server;

namespace Server.App.Hosting;

internal static class ServiceBindingConfigurator
{
    private static readonly Type LoginServiceType = LoadHotfixType("Server.Hotfix.Login.LoginService");
    private static readonly Type ChatServiceType = LoadHotfixType("Server.Hotfix.Chat.ChatService");

    private static Type LoadHotfixType(string typeName)
    {
        var hotfixDir = System.IO.Path.Combine(AppContext.BaseDirectory, "hotfix");
        var hotfixPath = System.IO.Path.Combine(hotfixDir, "Server.Hotfix.dll");
        var assembly = Assembly.LoadFrom(hotfixPath);
        return assembly.GetType(typeName, throwOnError: true)!;
    }

    public static void Bind(RpcServiceRegistry registry, IServiceProvider services)
    {
        LoginServiceBinder.BindFactory(
            registry,
            session =>
            {
                services.GetRequiredService<ChatConnectionLifecycle>().Track(session);
                return (ILoginService)ActivatorUtilities.CreateInstance(
                    services,
                    LoginServiceType,
                    new LoginCallbackProxy(session),
                    session.ContextId);
            });

        ChatServiceBinder.BindFactory(
            registry,
            session =>
            {
                services.GetRequiredService<ChatConnectionLifecycle>().Track(session);
                return (IChatService)ActivatorUtilities.CreateInstance(
                    services,
                    ChatServiceType,
                    new ChatCallbackProxy(session),
                    session.ContextId);
            });
    }
}
