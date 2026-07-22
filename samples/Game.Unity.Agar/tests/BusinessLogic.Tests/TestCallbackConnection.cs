using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Server;
using Lakona.Rpc.Transport.Loopback;
using Microsoft.Extensions.DependencyInjection;

namespace Agar.Unity.Tests;

internal sealed class TestCallbackConnection : IAsyncDisposable
{
    private readonly RpcSession clientSession;
    private readonly RpcSession serverSession;

    public TestCallbackConnection(
        IServiceProvider services,
        string connectionId,
        params object[] callbacks)
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        clientSession = new RpcSession(clientTransport, new JsonRpcSerializer(), $"{connectionId}-client");
        serverSession = new RpcSession(serverTransport, new JsonRpcSerializer(), connectionId);

        services.GetRequiredService<GameFrameworkConnectionRegistry>()
            .Set(connectionId, new RpcNotificationChannel(serverSession));
        services.GetRequiredService<GameSessionCallbackProxyRegistry>()
            .Add(new CallbackBinder(callbacks));
    }

    public async ValueTask DisposeAsync()
    {
        await serverSession.DisposeAsync().ConfigureAwait(false);
        await clientSession.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class CallbackBinder(object[] callbacks) : LakonaRpcServiceBinder
    {
        public override void Bind(LakonaGameServerRpcContext context)
        {
        }

        public override bool TryCreateCallback(
            Type callbackContractType,
            RpcNotificationChannel notifications,
            out object? callback)
        {
            callback = callbacks.FirstOrDefault(callbackContractType.IsInstanceOfType);
            return callback is not null;
        }
    }
}
