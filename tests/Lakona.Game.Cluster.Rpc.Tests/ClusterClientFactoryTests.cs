using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Core;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ClusterClientFactoryTests
{
    [Fact]
    public async Task GetClientAsyncPassesResolvedEndpointToTransportFactory()
    {
        var transportFactory = new RecordingTransportFactory();
        await using var factory = new ClusterClientFactory(
            transportFactory,
            new NoopSerializer());
        var target = new RouteLocation(
            "room/1",
            "node-b",
            new NodeEndpoint("tcp://127.0.0.1:20010"),
            DateTimeOffset.UtcNow.AddMinutes(1),
            nodeEpoch: 1,
            generation: 2);

        await factory.GetClientAsync(target, TestContext.Current.CancellationToken);

        var call = Assert.Single(transportFactory.Calls);
        Assert.Same(target, call.Target);
        Assert.Equal("tcp", call.Endpoint.Scheme);
        Assert.Equal("127.0.0.1", call.Endpoint.Host);
        Assert.Equal(20010, call.Endpoint.Port);
    }

    [Fact]
    public async Task GetClientAsyncReusesClientForSameNodeEpochAndEndpoint()
    {
        var transportFactory = new RecordingTransportFactory();
        await using var factory = new ClusterClientFactory(
            transportFactory,
            new NoopSerializer());
        var target = new RouteLocation(
            "room/1",
            "node-b",
            new NodeEndpoint("tcp://127.0.0.1:20010"),
            DateTimeOffset.UtcNow.AddMinutes(1),
            nodeEpoch: 1,
            generation: 1);

        var first = await factory.GetClientAsync(target, TestContext.Current.CancellationToken);
        var second = await factory.GetClientAsync(target, TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Single(transportFactory.Calls);
    }

    [Fact]
    public async Task GetClientAsyncReconnectsWhenNodeEpochChanges()
    {
        var transportFactory = new RecordingTransportFactory();
        await using var factory = new ClusterClientFactory(
            transportFactory,
            new NoopSerializer());

        var first = await factory.GetClientAsync(
            new RouteLocation(
                "room/1",
                "node-b",
                new NodeEndpoint("tcp://127.0.0.1:20010"),
                DateTimeOffset.UtcNow.AddMinutes(1),
                nodeEpoch: 1,
                generation: 1),
            TestContext.Current.CancellationToken);
        var second = await factory.GetClientAsync(
            new RouteLocation(
                "room/1",
                "node-b",
                new NodeEndpoint("tcp://127.0.0.1:20011"),
                DateTimeOffset.UtcNow.AddMinutes(1),
                nodeEpoch: 2,
                generation: 2),
            TestContext.Current.CancellationToken);

        Assert.NotSame(first, second);
        Assert.Equal(2, transportFactory.Calls.Count);
        Assert.Equal(20010, transportFactory.Calls[0].Endpoint.Port);
        Assert.Equal(20011, transportFactory.Calls[1].Endpoint.Port);
    }

    private sealed class RecordingTransportFactory : IClusterTransportFactory
    {
        public List<(RouteLocation Target, ClusterEndpoint Endpoint)> Calls { get; } = new();

        public ValueTask<ITransport> ConnectAsync(
            RouteLocation target,
            ClusterEndpoint endpoint,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add((target, endpoint));
            return ValueTask.FromResult<ITransport>(new IdleTransport());
        }
    }

    private sealed class IdleTransport : ITransport
    {
        public bool IsConnected { get; private set; }

        public ValueTask ConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsConnected = true;
            return default;
        }

        public ValueTask SendFrameAsync(
            ReadOnlyMemory<byte> frame,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return default;
        }

        public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(TransportFrame.Empty);
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return default;
        }
    }

    private sealed class NoopSerializer : IRpcSerializer
    {
        public TransportFrame SerializeFrame<T>(T value)
        {
            return TransportFrame.Empty;
        }

        public T Deserialize<T>(ReadOnlySpan<byte> payload)
        {
            throw new NotSupportedException();
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> payload)
        {
            throw new NotSupportedException();
        }
    }
}
