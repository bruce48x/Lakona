using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Net.Sockets.Kcp;
using Lakona.Rpc.Transport.Kcp;
using Xunit.Abstractions;

namespace Lakona.Rpc.Transport.Tests;

public sealed class KcpServerTransportRegressionTests(ITestOutputHelper output)
{
    [Fact]
    public async Task CancelingReceive_DoesNotDisconnectAndDisposalUnblocksNextReceive()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        await using var transport = new KcpServerTransport(socket, new IPEndPoint(IPAddress.Loopback, 1), 1);
        await transport.ConnectAsync();
        using var cancellation = new CancellationTokenSource();
        var receive = transport.ReceiveFrameAsync(cancellation.Token).AsTask();
        Assert.False(receive.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receive.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(transport.IsConnected);

        var next = transport.ReceiveFrameAsync().AsTask();
        Assert.False(next.IsCompleted);
        await transport.DisposeAsync();
        using var closed = await next.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(closed.IsEmpty);
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task OutputAllocation_DoesNotGrowWithPayloadSize()
    {
        using var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        using var receiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        receiver.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        receiver.ReceiveTimeout = 5000;
        await using var transport = new KcpServerTransport(sender, receiver.LocalEndPoint!, 1);
        var callback = (IKcpCallback)transport;
        MeasureOutputAllocation(callback, receiver, 32, 32);
        MeasureOutputAllocation(callback, receiver, 8192, 32);
        var small = MeasureOutputAllocation(callback, receiver, 32, 64);
        var large = MeasureOutputAllocation(callback, receiver, 8192, 64);
        output.WriteLine($"Runtime={Environment.Version}; OS={Environment.OSVersion}; CPUs={Environment.ProcessorCount}; serverGC={System.Runtime.GCSettings.IsServerGC}; warmup=32; samples=64; payload=32/8192; bytes/output={small}/{large}");
        Assert.True(large <= small + 512, $"Output allocation grew with payload size: {small} -> {large} bytes.");
    }

    private static long MeasureOutputAllocation(IKcpCallback callback, Socket receiver, int size, int samples)
    {
        var payload = new byte[size];
        Array.Fill(payload, (byte)42);
        var received = new byte[size];
        long allocated = 0;
        for (var i = 0; i < samples; i++)
        {
            var owner = new TrackingOwner(payload);
            var before = GC.GetAllocatedBytesForCurrentThread();
            callback.Output(owner, size);
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(1, owner.DisposeCount);
            Assert.Equal(size, receiver.Receive(received));
            Assert.Equal(payload, received);
        }
        return allocated / samples;
    }

    private sealed class TrackingOwner(byte[] bytes) : IMemoryOwner<byte>
    {
        public Memory<byte> Memory => bytes;
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }
}
