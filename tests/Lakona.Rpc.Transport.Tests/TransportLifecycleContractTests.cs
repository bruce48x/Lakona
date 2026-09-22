using System.Net;
using System.Net.Sockets;
using Lakona.Rpc.Core;
using Lakona.Rpc.Transport.Kcp;
using Lakona.Rpc.Transport.Loopback;
using Lakona.Rpc.Transport.Tcp;
using Lakona.Rpc.Transport.WebSocket;

namespace Lakona.Rpc.Transport.Tests;

public sealed class TransportLifecycleContractTests
{
    [Theory]
    [InlineData("tcp")]
    [InlineData("websocket")]
    [InlineData("kcp")]
    [InlineData("loopback")]
    public async Task Initialized_pair_supports_repeated_initialization_and_full_duplex_frames(string kind)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var pair = await TransportPair.CreateAsync(kind, deadline.Token);
        await pair.Client.ConnectAsync(deadline.Token);
        await pair.Server.ConnectAsync(deadline.Token);
        Assert.True(pair.Client.IsConnected);
        Assert.True(pair.Server.IsConnected);

        for (byte sequence = 1; sequence <= 3; sequence++)
        {
            var atClient = pair.Client.ReceiveFrameAsync(deadline.Token).AsTask();
            var atServer = pair.Server.ReceiveFrameAsync(deadline.Token).AsTask();
            Assert.False(atClient.IsCompleted);
            Assert.False(atServer.IsCompleted);
            await Task.WhenAll(
                pair.Client.SendFrameAsync(new byte[] { sequence, 10 }, deadline.Token).AsTask(),
                pair.Server.SendFrameAsync(new byte[] { sequence, 20 }, deadline.Token).AsTask());
            using var clientFrame = await atClient;
            using var serverFrame = await atServer;
            Assert.Equal(new byte[] { sequence, 20 }, clientFrame.ToArray());
            Assert.Equal(new byte[] { sequence, 10 }, serverFrame.ToArray());
        }
    }

    [Theory]
    [InlineData("tcp", false)]
    [InlineData("tcp", true)]
    [InlineData("websocket", false)]
    [InlineData("websocket", true)]
    [InlineData("kcp", false)]
    [InlineData("kcp", true)]
    [InlineData("loopback", false)]
    [InlineData("loopback", true)]
    public async Task Pending_receive_observes_cancellation(string kind, bool serverSide)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var pair = await TransportPair.CreateAsync(kind, deadline.Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var transport = serverSide ? pair.Server : pair.Client;
        var receive = transport.ReceiveFrameAsync(cancellation.Token).AsTask();
        Assert.False(receive.IsCompleted);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receive.WaitAsync(deadline.Token));
        // Cancellation does not promise that this connection can be reused.
    }

    [Theory]
    [InlineData("tcp")]
    [InlineData("websocket")]
    [InlineData("kcp")]
    [InlineData("loopback")]
    public async Task Disposal_after_io_has_drained_is_repeatable_and_disconnects(string kind)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var pair = await TransportPair.CreateAsync(kind, deadline.Token);
        await pair.Client.DisposeAsync().AsTask().WaitAsync(deadline.Token);
        await pair.Client.DisposeAsync().AsTask().WaitAsync(deadline.Token);
        Assert.False(pair.Client.IsConnected);
        await pair.Server.DisposeAsync().AsTask().WaitAsync(deadline.Token);
        await pair.Server.DisposeAsync().AsTask().WaitAsync(deadline.Token);
        Assert.False(pair.Server.IsConnected);
    }

    [Theory]
    [InlineData("tcp", false)]
    [InlineData("tcp", true)]
    [InlineData("websocket", false)]
    [InlineData("websocket", true)]
    [InlineData("kcp", false)]
    [InlineData("kcp", true)]
    [InlineData("loopback", false)]
    [InlineData("loopback", true)]
    public async Task Disposal_ends_an_idle_pending_receive(string kind, bool serverSide)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var pair = await TransportPair.CreateAsync(kind, deadline.Token);
        var transport = serverSide ? pair.Server : pair.Client;
        var receive = transport.ReceiveFrameAsync().AsTask();
        Assert.False(receive.IsCompleted);

        await transport.DisposeAsync().AsTask().WaitAsync(deadline.Token);
        var error = await Record.ExceptionAsync(async () =>
        {
            using var frame = await receive.WaitAsync(deadline.Token);
            Assert.True(frame.IsEmpty);
        });

        Assert.True(receive.IsCompleted, "Disposal must end the actual receive, not only the test's wait.");
        Assert.True(error is null or IOException or ObjectDisposedException or InvalidOperationException
            or OperationCanceledException or SocketException, error?.ToString());
        Assert.False(transport.IsConnected);
    }

    private sealed class TransportPair(ITransport client, ITransport server, IRpcConnectionAcceptor? acceptor = null)
        : IAsyncDisposable
    {
        public ITransport Client { get; } = client;
        public ITransport Server { get; } = server;

        public static async Task<TransportPair> CreateAsync(string kind, CancellationToken ct)
        {
            if (kind == "loopback")
            {
                LoopbackTransport.CreatePair(out var client, out var server);
                await client.ConnectAsync(ct);
                await server.ConnectAsync(ct);
                return new(client, server);
            }

            IRpcConnectionAcceptor acceptor = kind switch
            {
                "tcp" => new TcpConnectionAcceptor(0),
                "kcp" => new KcpConnectionAcceptor(0),
                "websocket" => await WsConnectionAcceptor.CreateAsync(FreeTcpPort(), "/ws", "127.0.0.1", 2, ct),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            var address = new Uri(acceptor.ListenAddress);
            ITransport transport = kind switch
            {
                "tcp" => new TcpTransport("127.0.0.1", address.Port),
                "kcp" => new KcpTransport("127.0.0.1", address.Port),
                _ => new WsTransport(address)
            };
            ITransport? accepted = null;
            try
            {
                await transport.ConnectAsync(ct);
                accepted = (await acceptor.AcceptAsync(ct)).Transport;
                await accepted.ConnectAsync(ct);
                return new(transport, accepted, acceptor);
            }
            catch
            {
                if (accepted is not null) await accepted.DisposeAsync();
                await transport.DisposeAsync();
                await acceptor.DisposeAsync();
                throw;
            }
        }

        private static int FreeTcpPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        public async ValueTask DisposeAsync()
        {
            try { await Client.DisposeAsync(); }
            finally
            {
                try { await Server.DisposeAsync(); }
                finally { if (acceptor is not null) await acceptor.DisposeAsync(); }
            }
        }
    }
}
