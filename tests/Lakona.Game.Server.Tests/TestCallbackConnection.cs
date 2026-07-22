using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Server;
using Lakona.Rpc.Transport.Loopback;

namespace Lakona.Game.Server.Tests;

internal sealed class TestCallbackConnection : IAsyncDisposable
{
    private readonly RpcSession clientSession;
    private readonly RpcSession serverSession;

    public TestCallbackConnection(
        IGameSessionRegistry sessions,
        string connectionId,
        params object[] callbacks)
        : this(
            sessions,
            new GameFrameworkConnectionRegistry(),
            new GameSessionCallbackProxyRegistry(),
            connectionId,
            callbacks)
    {
    }

    public TestCallbackConnection(
        IGameSessionRegistry sessions,
        GameFrameworkConnectionRegistry connections,
        GameSessionCallbackProxyRegistry callbackProxies,
        string connectionId,
        params object[] callbacks)
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        clientSession = new RpcSession(clientTransport, new JsonRpcSerializer(), $"{connectionId}-client");
        serverSession = new RpcSession(serverTransport, new JsonRpcSerializer(), connectionId);

        connections.Set(connectionId, new RpcNotificationChannel(serverSession));
        callbackProxies.Add(new CallbackBinder(callbacks));
        Resolver = new GameSessionCallbackResolver(sessions, connections, callbackProxies);
    }

    public GameSessionCallbackResolver Resolver { get; }

    public static GameSessionCallbackResolver CreateEmptyResolver(IGameSessionRegistry sessions) =>
        new(sessions, new GameFrameworkConnectionRegistry(), new GameSessionCallbackProxyRegistry());

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
