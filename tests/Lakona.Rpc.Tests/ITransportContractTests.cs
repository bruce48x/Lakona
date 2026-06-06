using Lakona.Rpc.Core;
using Lakona.Rpc.Transport.Loopback;

namespace Lakona.Rpc.Tests;

public sealed class ITransportContractTests
{
    [Fact]
    public async Task ConnectAsync_CanInitializeAcceptedOrInMemoryTransportIdempotently()
    {
        LoopbackTransport.CreatePair(out var client, out var server);

        Assert.False(client.IsConnected);
        Assert.False(server.IsConnected);

        await client.ConnectAsync();
        await client.ConnectAsync();
        await server.ConnectAsync();
        await server.ConnectAsync();

        Assert.True(client.IsConnected);
        Assert.True(server.IsConnected);

        await client.DisposeAsync();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task TransformingTransport_ConnectAsyncDelegatesToInnerTransport()
    {
        var inner = new TrackingTransport();
        await using var transport = new TransformingTransport(inner, new TransportSecurityConfig
        {
            EnableCompression = true
        });

        await transport.ConnectAsync();
        await transport.ConnectAsync();

        Assert.Equal(2, inner.ConnectCount);
        Assert.True(transport.IsConnected);
    }

    private sealed class TrackingTransport : ITransport
    {
        public int ConnectCount { get; private set; }

        public bool IsConnected { get; private set; }

        public ValueTask ConnectAsync(CancellationToken ct = default)
        {
            ConnectCount++;
            IsConnected = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            return ValueTask.FromResult(TransportFrame.Empty);
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }
}
