using Lakona.Game.Abstractions;
using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Client;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Server;
using Lakona.Rpc.Transport.Loopback;

namespace Lakona.Game.Server.Tests;

internal sealed class TestFrameworkNotificationConnection : IAsyncDisposable
{
    private readonly RpcClientRuntime client;
    private readonly RpcSession server;

    private TestFrameworkNotificationConnection(
        RpcClientRuntime client,
        RpcSession server,
        Task<SessionTerminationNotice> terminationNotice)
    {
        this.client = client;
        this.server = server;
        TerminationNotice = terminationNotice;
    }

    public Task<SessionTerminationNotice> TerminationNotice { get; }

    public static async ValueTask<TestFrameworkNotificationConnection> CreateAsync(
        GameFrameworkConnectionRegistry connections,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var client = new RpcClientRuntime(clientTransport, new JsonRpcSerializer());
        var server = new RpcSession(serverTransport, new JsonRpcSerializer(), connectionId);
        var terminationNotice = SessionTerminationNoticeCapture.Register(client);

        try
        {
            await client.StartAsync(cancellationToken).ConfigureAwait(false);
            await server.StartAsync(cancellationToken).ConfigureAwait(false);
            connections.Set(connectionId, new RpcNotificationChannel(server));
            return new TestFrameworkNotificationConnection(client, server, terminationNotice);
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await server.DisposeAsync().ConfigureAwait(false);
        await client.DisposeAsync().ConfigureAwait(false);
    }
}
