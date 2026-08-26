using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Core;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ClusterClientFactoryTests
{
    [Fact]
    public async Task GetClientAsync_propagates_the_server_logger_factory_to_outbound_rpc_clients()
    {
        var loggerFactory = new RecordingLoggerFactory();
        await using var factory = new ClusterClientFactory(
            CreateChannel(new RecordingTransportFactory()),
            loggerFactory: loggerFactory);

        await factory.GetClientAsync(
            CreateTarget("tcp://127.0.0.1:20010", "bbbbbbbb-0000-0000-0000-000000000001"),
            TestContext.Current.CancellationToken);

        Assert.Contains("Lakona.Rpc.Client.Request", loggerFactory.Categories);
    }

    [Fact]
    public async Task GetClientAsyncPassesResolvedEndpointToTransportFactory()
    {
        var transportFactory = new RecordingTransportFactory();
        await using var factory = new ClusterClientFactory(
            CreateChannel(transportFactory));
        var target = CreateTarget("tcp://127.0.0.1:20010", "bbbbbbbb-0000-0000-0000-000000000001");

        await factory.GetClientAsync(target, TestContext.Current.CancellationToken);

        var call = Assert.Single(transportFactory.Calls);
        Assert.Equal("tcp", call.Scheme);
        Assert.Equal("127.0.0.1", call.Host);
        Assert.Equal(20010, call.Port);
    }

    [Fact]
    public async Task Formation_contacts_are_cached_by_endpoint_without_route_identity()
    {
        var transportFactory = new RecordingTransportFactory();
        await using var factory = new ClusterClientFactory(
            CreateChannel(transportFactory));
        var contact = new NodeEndpoint("tcp://127.0.0.1:20010");

        var first = await factory.GetClientAsync(contact, TestContext.Current.CancellationToken);
        var second = await factory.GetClientAsync(contact, TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        var call = Assert.Single(transportFactory.Calls);
        Assert.Equal("127.0.0.1", call.Host);
        Assert.Equal(20010, call.Port);
    }

    [Fact]
    public async Task GetClientAsyncReusesClientForSameNodeIncarnationAndEndpoint()
    {
        var transportFactory = new RecordingTransportFactory();
        await using var factory = new ClusterClientFactory(
            CreateChannel(transportFactory));
        var target = CreateTarget("tcp://127.0.0.1:20010", "bbbbbbbb-0000-0000-0000-000000000001");

        var first = await factory.GetClientAsync(target, TestContext.Current.CancellationToken);
        var second = await factory.GetClientAsync(target, TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Single(transportFactory.Calls);
    }

    [Fact]
    public async Task GetClientAsyncReconnectsWhenNodeIncarnationChanges()
    {
        var transportFactory = new RecordingTransportFactory();
        await using var factory = new ClusterClientFactory(
            CreateChannel(transportFactory));

        var first = await factory.GetClientAsync(
            CreateTarget("tcp://127.0.0.1:20010", "bbbbbbbb-0000-0000-0000-000000000001"),
            TestContext.Current.CancellationToken);
        var second = await factory.GetClientAsync(
            CreateTarget("tcp://127.0.0.1:20011", "bbbbbbbb-0000-0000-0000-000000000002"),
            TestContext.Current.CancellationToken);

        Assert.NotSame(first, second);
        Assert.Equal(2, transportFactory.Calls.Count);
        Assert.Equal(20010, transportFactory.Calls[0].Port);
        Assert.Equal(20011, transportFactory.Calls[1].Port);
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
        var target = CreateTarget("tcp://127.0.0.1:20010", "bbbbbbbb-0000-0000-0000-000000000001");

        var calls = Enumerable.Range(0, 32)
            .Select(_ => factory.GetClientAsync(target, TestContext.Current.CancellationToken).AsTask())
            .ToArray();
        await transportFactory.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        transportFactory.Release.SetResult();
        var clients = await Task.WhenAll(calls);

        Assert.Equal(1, transportFactory.ConnectCount);
        Assert.All(clients, client => Assert.Same(clients[0], client));
    }

    [Fact]
    public async Task Disconnected_client_is_replaced_once_for_concurrent_callers()
    {
        var transportFactory = new RecordingTransportFactory();
        await using var factory = new ClusterClientFactory(
            CreateChannel(transportFactory));
        var target = CreateTarget("tcp://127.0.0.1:20010", "bbbbbbbb-0000-0000-0000-000000000001");
        var first = await factory.GetClientAsync(target, TestContext.Current.CancellationToken);
        var firstRuntime = Assert.IsType<Lakona.Rpc.Client.RpcClientRuntime>(first);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        firstRuntime.Disconnected += _ => disconnected.TrySetResult();

        transportFactory.Transports[0].Disconnect();
        await disconnected.Task.WaitAsync(TestContext.Current.CancellationToken);
        var calls = Enumerable.Range(0, 32)
            .Select(_ => factory.GetClientAsync(target, TestContext.Current.CancellationToken).AsTask())
            .ToArray();
        var replacements = await Task.WhenAll(calls);

        Assert.Equal(2, transportFactory.Calls.Count);
        Assert.All(replacements, replacement => Assert.Same(replacements[0], replacement));
        Assert.NotSame(first, replacements[0]);
        await transportFactory.Transports[0].Disposed.Task.WaitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Dispose_cancels_a_shared_connection_attempt_without_waiting_for_connect_timeout()
    {
        var transportFactory = new BlockingTransportFactory();
        var factory = new ClusterClientFactory(
            CreateChannel(transportFactory),
            new ClusterClientFactoryOptions { ConnectTimeout = TimeSpan.FromMinutes(1) });
        var call = factory.GetClientAsync(
            CreateTarget("tcp://127.0.0.1:20010", "bbbbbbbb-0000-0000-0000-000000000001"),
            TestContext.Current.CancellationToken).AsTask();
        await transportFactory.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        await factory.DisposeAsync().AsTask().WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
    }

    private sealed class RecordingTransportFactory : IClusterRpcTransport
    {
        public string Scheme => "tcp";

        public List<ClusterEndpoint> Calls { get; } = new();

        public List<IdleTransport> Transports { get; } = new();

        public ValueTask<ITransport> ConnectAsync(
            ClusterEndpoint endpoint,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(endpoint);
            var transport = new IdleTransport();
            Transports.Add(transport);
            return new ValueTask<ITransport>(transport);
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
            ClusterEndpoint endpoint,
            CancellationToken cancellationToken = default)
        {
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
        private readonly TaskCompletionSource _disconnect = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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

        public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = Interlocked.Exchange(ref _negotiationRequest, null);
            if (request is not null)
            {
                var response = request.ToArray();
                response[5] = 2;
                return TransportFrame.CopyOf(response);
            }

            await _disconnect.Task.WaitAsync(cancellationToken);
            return TransportFrame.Empty;
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            _disconnect.TrySetResult();
            Disposed.TrySetResult();
            return default;
        }

        public void Disconnect()
        {
            IsConnected = false;
            _disconnect.TrySetResult();
        }
    }

    private static ClusterRpcChannel CreateChannel(IClusterRpcTransport transport) =>
        new(transport, new NoopSerializer(), "lakona.cluster.test.v1");

    private static RouteLocation CreateTarget(string endpoint, string incarnation) => new(
        new RouteKey("room/1"),
        new NodeReference(
            new ClusterIncarnationId(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000000")),
            new NodeId("node-b"),
            new NodeIncarnationId(Guid.Parse(incarnation))),
        new MembershipViewId(1),
        new NodeEndpoint(endpoint));

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

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        public List<string> Categories { get; } = new();

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            Categories.Add(categoryName);
            return Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        }

        public void Dispose()
        {
        }
    }
}
