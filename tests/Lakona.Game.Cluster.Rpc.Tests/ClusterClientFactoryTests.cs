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
            CreateChannel(transportFactory));
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
            CreateChannel(transportFactory));
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
            CreateChannel(transportFactory));

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

    [Fact]
    public async Task GetClientAsyncDoesNotReuseAClientAcrossNodeIncarnations()
    {
        var transportFactory = new RecordingTransportFactory();
        await using var factory = new ClusterClientFactory(
            CreateChannel(transportFactory));
        var cluster = new ClusterIncarnationId(
            Guid.Parse("aaaaaaaa-1111-2222-3333-aaaaaaaaaaaa"));
        var endpoint = new NodeEndpoint("tcp://127.0.0.1:20010");

        var first = await factory.GetClientAsync(
            new RouteLocation(
                "room/1",
                new NodeReference(
                    cluster,
                    new NodeId("node-b"),
                    new NodeIncarnationId(
                        Guid.Parse("bbbbbbbb-1111-2222-3333-bbbbbbbbbbbb"))),
                new MembershipViewId(1),
                endpoint),
            TestContext.Current.CancellationToken);
        var second = await factory.GetClientAsync(
            new RouteLocation(
                "room/1",
                new NodeReference(
                    cluster,
                    new NodeId("node-b"),
                    new NodeIncarnationId(
                        Guid.Parse("cccccccc-1111-2222-3333-cccccccccccc"))),
                new MembershipViewId(2),
                endpoint),
            TestContext.Current.CancellationToken);

        Assert.NotSame(first, second);
        Assert.Equal(2, transportFactory.Calls.Count);
    }

    [Fact]
    public async Task Concurrent_cache_misses_share_one_connection_attempt()
    {
        var transportFactory = new BlockingTransportFactory();
        await using var factory = new ClusterClientFactory(
            CreateChannel(transportFactory));
        var target = new RouteLocation(
            "room/1",
            "node-b",
            new NodeEndpoint("tcp://127.0.0.1:20010"),
            DateTimeOffset.UtcNow.AddMinutes(1),
            nodeEpoch: 1,
            generation: 1);

        var calls = Enumerable.Range(0, 32)
            .Select(_ => factory.GetClientAsync(target, TestContext.Current.CancellationToken).AsTask())
            .ToArray();
        await transportFactory.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        transportFactory.Release.SetResult();
        var clients = await Task.WhenAll(calls);

        Assert.Equal(1, transportFactory.ConnectCount);
        Assert.All(clients, client => Assert.Same(clients[0], client));
    }

    private sealed class RecordingTransportFactory : IClusterRpcTransport
    {
        public string Scheme => "tcp";

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

        public ValueTask<IRpcConnectionAcceptor> ListenAsync(
            ClusterEndpoint endpoint,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class BlockingTransportFactory : IClusterRpcTransport
    {
        public string Scheme => "tcp";

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ConnectCount;

        public async ValueTask<ITransport> ConnectAsync(
            RouteLocation target,
            ClusterEndpoint endpoint,
            CancellationToken cancellationToken = default)
        {
            _ = target;
            _ = endpoint;
            Interlocked.Increment(ref ConnectCount);
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new IdleTransport();
        }

        public ValueTask<IRpcConnectionAcceptor> ListenAsync(
            ClusterEndpoint endpoint,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class IdleTransport : ITransport
    {
        private byte[]? _negotiationRequest;

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
            _negotiationRequest = frame.ToArray();
            return default;
        }

        public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = _negotiationRequest?.ToArray()
                ?? throw new InvalidOperationException("A negotiation request was not sent.");
            response[5] = 2;
            return ValueTask.FromResult(TransportFrame.CopyOf(response));
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return default;
        }
    }

    private static ClusterRpcChannel CreateChannel(IClusterRpcTransport transport) =>
        new(transport, new NoopSerializer(), "lakona.cluster.test.v1");

    private sealed class NoopSerializer : IRpcSerializer
    {
        public void Serialize<T>(
            System.Buffers.IBufferWriter<byte> destination,
            T value)
        {
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
