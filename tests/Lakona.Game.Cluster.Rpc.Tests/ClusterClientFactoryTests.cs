using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Microsoft.Extensions.Logging;
using Lakona.Rpc.Transport.Tcp;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ClusterClientFactoryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private const string TestProtocol = "lakona.cluster.test.v1";

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Overlapping_incarnation_requests_do_not_dispose_a_client_before_its_call(bool requestPreviousAgain)
    {
        var transportFactory = new RecordingTransportFactory();
        await using var factory = new ClusterClientFactory(CreateChannel(transportFactory));
        var previousTarget = CreateTarget("tcp://127.0.0.1:20010", "bbbbbbbb-0000-0000-0000-000000000001");
        var currentTarget = CreateTarget("tcp://127.0.0.1:20010", "bbbbbbbb-0000-0000-0000-000000000002");
        var previous = Assert.IsType<RpcClientRuntime>(await factory.GetClientAsync(previousTarget, TestContext.Current.CancellationToken));
        var current = Assert.IsType<RpcClientRuntime>(await factory.GetClientAsync(currentTarget, TestContext.Current.CancellationToken));
        if (requestPreviousAgain)
        {
            // Directory handoff can still contact an old owner after a new view is installed.
            await factory.GetClientAsync(previousTarget, TestContext.Current.CancellationToken);
        }
        foreach (var transport in transportFactory.Transports) transport.RespondToRequests = true;

        // A different exact incarnation must not invalidate a client already handed to a caller.
        var selected = requestPreviousAgain ? current : previous;
        using var response = await selected.CallRawAsync(1, 1, new byte[] { 42 }, TestContext.Current.CancellationToken)
            .AsTask().WaitAsync(Deadline, TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 42 }, response.Memory.ToArray());
        Assert.Equal(2, transportFactory.Calls.Count);
        Assert.Same(current, await factory.GetClientAsync(currentTarget, TestContext.Current.CancellationToken));

        // Disconnection, rather than another incarnation's lookup, still triggers cleanup.
        transportFactory.Transports[0].Disconnect();
        await transportFactory.Transports[0].Disposed.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        Assert.Equal(RpcClientConnectionState.Connected, current.ConnectionState);
    }

    [Fact]
    public async Task Fixed_TCP_endpoint_restarts_reclaim_connections_including_late_previous_incarnation_lookups()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var transport = new RecordingTcpTransport();
        await using var factory = new ClusterClientFactory(CreateChannel(transport));
        RouteLocation? previous = null;
        var port = 0;
        for (var generation = 0; generation < 10; generation++)
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var target = CreateTarget($"tcp://127.0.0.1:{port}", Guid.NewGuid().ToString());
            var servers = new List<TcpServerTransport>();
            try
            {
                var current = await ConnectAsync(() => factory.GetClientAsync(target, ct));
                var contact = await ConnectAsync(() => factory.GetClientAsync(target.Endpoint, ct));
                Assert.Same(current, await factory.GetClientAsync(target, ct));
                Assert.Same(contact, await factory.GetClientAsync(target.Endpoint, ct));
                if (previous is not null)
                {
                    // The old key reconnects to the same endpoint, now served by the new process.
                    var late = await ConnectAsync(() => factory.GetClientAsync(previous, ct));
                    Assert.NotSame(current, late);
                    Assert.Same(late, await factory.GetClientAsync(previous, ct));
                    Assert.Same(current, await factory.GetClientAsync(target, ct));
                }

                Assert.Equal(generation == 0 ? 2 : 3, transport.Connections.Count(connection => !connection.Disposed.Task.IsCompleted));
                Assert.Equal(2 + generation * 3, transport.Connections.Count);
                Assert.All(transport.Connections.Where(connection => !connection.Disposed.Task.IsCompleted),
                    connection => Assert.True(connection.IsConnected));
                listener.Stop();
                if (generation == 9)
                {
                    // Final factory shutdown must close live sockets without a peer disconnect.
                    await factory.DisposeAsync().AsTask().WaitAsync(Deadline, ct);
                    foreach (var server in servers)
                    {
                        await Assert.ThrowsAnyAsync<IOException>(async () =>
                        {
                            using var unexpected = await server.ReceiveFrameAsync(ct);
                        });
                    }
                }
                else
                {
                    foreach (var server in servers) await server.DisposeAsync();
                }
                await Task.WhenAll(transport.Connections.Select(connection => connection.Disposed.Task)).WaitAsync(Deadline, ct);
                Assert.All(transport.Connections, connection => Assert.Equal(1, connection.DisposeCount));
                Assert.All(transport.Connections, connection => Assert.False(connection.IsConnected));
                previous = target;
            }
            finally
            {
                foreach (var server in servers) await server.DisposeAsync();
            }

            async Task<IRpcClient> ConnectAsync(Func<ValueTask<IRpcClient>> acquire)
            {
                var connecting = acquire().AsTask();
                var server = new TcpServerTransport(await listener.AcceptTcpClientAsync(ct));
                servers.Add(server);
                await ClusterRpcProtocolNegotiation.NegotiateServerAsync(server, TestProtocol, ct);
                return await connecting.WaitAsync(Deadline, ct);
            }
        }
        Assert.Equal(29, transport.Connections.Count);
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

    [Fact]
    public async Task Stopped_client_is_replaced_before_response_drain_without_replaying_the_request()
    {
        var transportFactory = new RecordingTransportFactory();
        await using var factory = new ClusterClientFactory(CreateChannel(transportFactory));
        var target = CreateTarget("tcp://127.0.0.1:20010", "bbbbbbbb-0000-0000-0000-000000000001");
        var context = new PausedContext();
        var first = Assert.IsType<RpcClientRuntime>(await GetClientWithContext(factory, target, context));
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.ConnectionStateChanged += change =>
        {
            if (change.CurrentState == RpcClientConnectionState.Stopped) stopped.TrySetResult();
            if (change.CurrentState == RpcClientConnectionState.Disposed) disposed.TrySetResult();
        };
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Disconnected += _ => disconnected.TrySetResult();
        var transport = transportFactory.Transports[0];
        transport.RespondToRequests = true;
        var pending = first.CallRawAsync(1, 1, new byte[] { 42 }, TestContext.Current.CancellationToken).AsTask();
        await context.Posted.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        transport.Disconnect();
        await stopped.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);

        var calls = Enumerable.Range(0, 32)
            .Select(_ => factory.GetClientAsync(target, TestContext.Current.CancellationToken).AsTask());
        var replacements = await Task.WhenAll(calls).WaitAsync(Deadline, TestContext.Current.CancellationToken);
        Assert.Equal(2, transportFactory.Calls.Count);
        Assert.All(replacements, replacement => Assert.Same(replacements[0], replacement));
        Assert.NotSame(first, replacements[0]);
        Assert.False(pending.IsCompleted);
        Assert.False(disconnected.Task.IsCompleted);
        Assert.False(transport.Disposed.Task.IsCompleted);
        Assert.Equal(1, transport.RequestCount);
        Assert.Equal(0, transportFactory.Transports[1].RequestCount);

        context.Run();
        using var response = await pending.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 42 }, response.Memory.ToArray());
        await disconnected.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        await transport.Disposed.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        await disposed.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        Assert.Same(replacements[0], await factory.GetClientAsync(target, TestContext.Current.CancellationToken));
        Assert.Equal(2, transportFactory.Calls.Count);
    }

    [Fact]
    public async Task Disposed_client_is_replaced_while_its_transport_cleanup_is_pending()
    {
        var transportFactory = new RecordingTransportFactory();
        await using var factory = new ClusterClientFactory(CreateChannel(transportFactory));
        var target = CreateTarget("tcp://127.0.0.1:20010", "bbbbbbbb-0000-0000-0000-000000000001");
        var first = Assert.IsType<RpcClientRuntime>(await factory.GetClientAsync(target, TestContext.Current.CancellationToken));
        var transport = transportFactory.Transports[0];
        transport.DisposeRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposal = first.DisposeAsync().AsTask();
        try
        {
            // Get immediately: cache admission must use the state even if notification delivery lags.
            var replacement = await factory.GetClientAsync(target, TestContext.Current.CancellationToken)
                .AsTask().WaitAsync(Deadline, TestContext.Current.CancellationToken);
            Assert.NotSame(first, replacement);
            Assert.Equal(RpcClientConnectionState.Connected, Assert.IsType<RpcClientRuntime>(replacement).ConnectionState);
            Assert.False(disposal.IsCompleted);
            Assert.Equal(2, transportFactory.Calls.Count);
        }
        finally
        {
            transport.DisposeRelease.TrySetResult();
            await disposal.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Factory_disposal_cancels_and_joins_evicted_clients_still_draining_responses()
    {
        var transportFactory = new RecordingTransportFactory();
        await using var factory = new ClusterClientFactory(CreateChannel(transportFactory));
        var target = CreateTarget("tcp://127.0.0.1:20010", "bbbbbbbb-0000-0000-0000-000000000001");
        var context = new PausedContext();
        var first = Assert.IsType<RpcClientRuntime>(await GetClientWithContext(factory, target, context));
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.ConnectionStateChanged += change =>
        {
            if (change.CurrentState == RpcClientConnectionState.Stopped) stopped.TrySetResult();
        };
        var transport = transportFactory.Transports[0];
        transport.RespondToRequests = true;
        transport.DisposeRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = first.CallRawAsync(1, 1, new byte[] { 42 }, TestContext.Current.CancellationToken).AsTask();
        await context.Posted.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        transport.Disconnect();
        await stopped.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        await factory.GetClientAsync(target, TestContext.Current.CancellationToken);

        var disposal = factory.DisposeAsync().AsTask();
        try
        {
            await transport.Disposing.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            Assert.False(disposal.IsCompleted);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => pending.WaitAsync(Deadline, TestContext.Current.CancellationToken));
        }
        finally
        {
            transport.DisposeRelease.TrySetResult();
            await disposal.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        }

        Assert.All(transportFactory.Transports, item => Assert.True(item.Disposed.Task.IsCompleted));
        Assert.All(transportFactory.Transports, item => Assert.Equal(1, item.DisposeCount));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => factory.GetClientAsync(target, TestContext.Current.CancellationToken).AsTask());
    }

    private static Task<IRpcClient> GetClientWithContext(
        ClusterClientFactory factory, RouteLocation target, SynchronizationContext context)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            return factory.GetClientAsync(target, TestContext.Current.CancellationToken).AsTask();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class PausedContext : SynchronizationContext
    {
        private SendOrPostCallback? _callback;
        private object? _state;
        public TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            _callback = callback;
            _state = state;
            Posted.TrySetResult();
        }
        public void Run() => _callback!(_state);
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

    private sealed class RecordingTcpTransport : IClusterRpcTransport
    {
        public string Scheme => "tcp";
        public List<RecordedConnection> Connections { get; } = new();

        public async ValueTask<ITransport> ConnectAsync(ClusterEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            var connection = new RecordedConnection(await TcpClusterRpcTransport.Default.ConnectAsync(endpoint, cancellationToken));
            Connections.Add(connection);
            return connection;
        }

        public ValueTask<IRpcConnectionAcceptor> ListenAsync(ClusterEndpoint endpoint, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordedConnection(ITransport inner) : ITransport
    {
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposeCount;
        public bool IsConnected => inner.IsConnected;
        public ValueTask ConnectAsync(CancellationToken cancellationToken) => inner.ConnectAsync(cancellationToken);
        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken) => inner.SendFrameAsync(frame, cancellationToken);
        public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken cancellationToken) => inner.ReceiveFrameAsync(cancellationToken);

        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref DisposeCount);
            await inner.DisposeAsync();
            Disposed.TrySetResult();
        }
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
        private bool _negotiated;
        private readonly Channel<TransportFrame> _responses = Channel.CreateUnbounded<TransportFrame>();

        public bool RespondToRequests { get; set; }
        public int RequestCount;
        public int DisposeCount;
        public TaskCompletionSource? DisposeRelease { get; set; }
        public TaskCompletionSource Disposing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
            if (!_negotiated)
            {
                _negotiationRequest = frame.ToArray();
            }
            else if (RpcEnvelopeCodec.PeekFrameType(frame.Span) == RpcFrameType.Request)
            {
                Interlocked.Increment(ref RequestCount);
                if (RespondToRequests)
                {
                    using var bytes = TransportFrame.CopyOf(frame.Span);
                    using var request = RpcEnvelopeCodec.DecodeRequest(bytes);
                    _responses.Writer.TryWrite(RpcEnvelopeCodec.EncodeResponse(
                        request.RequestId, RpcStatus.Ok, request.Payload.Memory));
                }
            }
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
                _negotiated = true;
                return TransportFrame.CopyOf(response);
            }

            if (await _responses.Reader.WaitToReadAsync(cancellationToken) && _responses.Reader.TryRead(out var frame))
            {
                return frame;
            }
            return TransportFrame.Empty;
        }

        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref DisposeCount);
            Disposing.TrySetResult();
            if (DisposeRelease is not null) await DisposeRelease.Task;
            IsConnected = false;
            _responses.Writer.TryComplete();
            Disposed.TrySetResult();
        }

        public void Disconnect()
        {
            IsConnected = false;
            _responses.Writer.TryComplete();
        }
    }

    private static ClusterRpcChannel CreateChannel(IClusterRpcTransport transport) =>
        new(transport, new NoopSerializer(), TestProtocol);

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
