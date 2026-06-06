using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Lakona.Rpc.Core;
using Lakona.Rpc.Transport.Tcp;

namespace Lakona.Rpc.Tests;

public sealed class TcpTransportTests
{
    [Fact]
    public async Task ConnectAsync_WhenCanceledBeforeConnect_DoesNotOpenConnection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        await using var client = new TcpTransport(IPAddress.Loopback.ToString(), endpoint.Port);
        var acceptTask = listener.AcceptTcpClientAsync();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ConnectAsync(cts.Token).AsTask());
        await Task.Delay(100);

        Assert.False(client.IsConnected);
        Assert.False(acceptTask.IsCompleted);
    }

    [Fact]
    public async Task ClientAndServerTransports_CanRoundTripFrames()
    {
        await using var pair = await ConnectedPair.CreateAsync();

        var clientPayload = new byte[] { 1, 2, 3, 4 };
        await pair.Client.SendFrameAsync(clientPayload);
        Assert.Equal(clientPayload, (await pair.Server.ReceiveFrameAsync()).ToArray());

        await pair.Server.SendFrameAsync(ReadOnlyMemory<byte>.Empty);
        Assert.Empty((await pair.Client.ReceiveFrameAsync()).ToArray());
    }

    [Fact]
    public async Task AcceptedServerTransport_IsConnectedBeforeConnectAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        await using var client = new TcpTransport(IPAddress.Loopback.ToString(), endpoint.Port);
        var acceptTask = listener.AcceptTcpClientAsync();

        await client.ConnectAsync();
        var acceptedClient = await acceptTask;

        await using var server = new TcpServerTransport(acceptedClient);

        Assert.True(server.IsConnected);

        listener.Stop();
    }

    [Fact]
    public async Task ReceiveFrameAsync_CanReadBackToBackFrames()
    {
        await using var pair = await ConnectedPair.CreateAsync();

        await pair.Client.SendFrameAsync(new byte[] { 10, 11, 12 });
        await pair.Client.SendFrameAsync(new byte[] { 20, 21 });

        Assert.Equal(new byte[] { 10, 11, 12 }, (await pair.Server.ReceiveFrameAsync()).ToArray());
        Assert.Equal(new byte[] { 20, 21 }, (await pair.Server.ReceiveFrameAsync()).ToArray());
    }

    [Fact]
    public async Task ReceiveFrameAsync_WhenRemoteCloses_ThrowsIOException()
    {
        await using var pair = await ConnectedPair.CreateAsync();

        await pair.Client.DisposeAsync();

        await Assert.ThrowsAsync<IOException>(() => pair.Server.ReceiveFrameAsync().AsTask());
    }

    [Fact]
    public async Task SendFrameAsync_WhenFrameExceedsMaxSize_Throws()
    {
        var oversizedPayload = GC.AllocateUninitializedArray<byte>(LengthPrefix.DefaultMaxFrameSize + 1);
        var framingType = typeof(TcpTransport).Assembly.GetType("Lakona.Rpc.Transport.Tcp.TcpPipeFraming");
        Assert.NotNull(framingType);

        await using var stream = new RecordingStream();
        var framing = Activator.CreateInstance(
            framingType!,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: new object[] { stream, LengthPrefix.DefaultMaxFrameSize },
            culture: null);
        Assert.NotNull(framing);

        var sendMethod = framingType!.GetMethod(nameof(TcpTransport.SendFrameAsync), new[]
        {
            typeof(ReadOnlyMemory<byte>),
            typeof(CancellationToken)
        });
        Assert.NotNull(sendMethod);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var sendTask = (ValueTask)sendMethod!.Invoke(framing, new object[] { new ReadOnlyMemory<byte>(oversizedPayload), CancellationToken.None })!;
            await sendTask.AsTask();
        });
        Assert.Contains("Frame too large", ex.Message, StringComparison.OrdinalIgnoreCase);

        await ((IAsyncDisposable)framing!).DisposeAsync();
    }

    private sealed class ConnectedPair : IAsyncDisposable
    {
        private ConnectedPair(TcpListener listener, TcpTransport client, TcpServerTransport server)
        {
            _listener = listener;
            Client = client;
            Server = server;
        }

        private readonly TcpListener _listener;

        public TcpTransport Client { get; }
        public TcpServerTransport Server { get; }

        public static async Task<ConnectedPair> CreateAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var client = new TcpTransport(IPAddress.Loopback.ToString(), endpoint.Port);
            var acceptTask = listener.AcceptTcpClientAsync();

            await client.ConnectAsync();
            var acceptedClient = await acceptTask;

            var server = new TcpServerTransport(acceptedClient);
            await server.ConnectAsync();

            return new ConnectedPair(listener, client, server);
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await Server.DisposeAsync();
            _listener.Stop();
        }
    }

    private sealed class RecordingStream : MemoryStream
    {
        public override bool CanRead => false;

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override int Read(Span<byte> buffer)
        {
            throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
