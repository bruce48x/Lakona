using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Lakona.Rpc.Serializer.Json;

[assembly: RpcGeneratedServicesBinder(typeof(Lakona.Rpc.Tests.TestGeneratedBinder))]

namespace Lakona.Rpc.Tests;

public class RpcServerHostBuilderTests
{
    [Fact]
    public void UseCommandLine_ParsesPortCompressionAndEncryption()
    {
        var builder = RpcServerHostBuilder.Create()
            .UseCommandLine(["21000", "--compress-threshold", "4096", "--encrypt-key", "AQIDBA=="]);

        Assert.Equal(21000, builder.Port);
        Assert.True(builder.Security.EnableCompression);
        Assert.Equal(4096, builder.Security.CompressionThresholdBytes);
        Assert.True(builder.Security.EnableEncryption);
        Assert.Equal("AQIDBA==", builder.Security.EncryptionKeyBase64);
    }

    [Fact]
    public void UseKeepAlive_SetsBuilderOptions()
    {
        var builder = RpcServerHostBuilder.Create()
            .UseKeepAlive(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));

        Assert.True(builder.KeepAlive.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(5), builder.KeepAlive.Interval);
        Assert.Equal(TimeSpan.FromSeconds(15), builder.KeepAlive.Timeout);
        Assert.False(builder.KeepAlive.MeasureRtt);
    }

    [Fact]
    public void UseCommandLine_ParsesKeepAliveOptions()
    {
        var builder = RpcServerHostBuilder.Create()
            .UseCommandLine(["--keepalive-interval", "00:00:05", "--keepalive-timeout", "00:00:15"]);

        Assert.True(builder.KeepAlive.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(5), builder.KeepAlive.Interval);
        Assert.Equal(TimeSpan.FromSeconds(15), builder.KeepAlive.Timeout);
        Assert.False(builder.KeepAlive.MeasureRtt);
    }

    [Fact]
    public void UseLimits_UpdatesBuilderLimits()
    {
        var builder = RpcServerHostBuilder.Create()
            .UseLimits(limits =>
            {
                limits.MaxConcurrentRequestsPerSession = 8;
                limits.MaxQueuedRequestsPerSession = 32;
                limits.MaxPendingAcceptedConnections = 12;
            });

        Assert.Equal(8, builder.Limits.MaxConcurrentRequestsPerSession);
        Assert.Equal(32, builder.Limits.MaxQueuedRequestsPerSession);
        Assert.Equal(12, builder.Limits.MaxPendingAcceptedConnections);
    }

    [Fact]
    public void Build_WhenServicesConfiguredExplicitly_DoesNotRequireGeneratedBinderDiscovery()
    {
        var builder = RpcServerHostBuilder.Create()
            .UseSerializer(new JsonRpcSerializer())
            .UseAcceptor(_ => ValueTask.FromResult<IRpcConnectionAcceptor>(new NoopConnectionAcceptor()))
            .ConfigureServices(_ => { });

        var host = builder.Build();

        Assert.NotNull(host);
    }

    [Fact]
    public async Task RunAsync_CancelWithNoActiveConnections_CompletesCleanly()
    {
        // Regression: BoundedConnectionAcceptor.DisposeAsync() calls _inner.DisposeAsync(),
        // but the caller also held an "await using var baseAcceptor" — double-Dispose caused
        // ObjectDisposedException on the accepting side (reproduced with KCP server, no clients).
        using var cts = new CancellationTokenSource();
        var acceptorDisposed = 0;
        var acceptor = new TrackingNeverAcceptAcceptor(() => Interlocked.Increment(ref acceptorDisposed));

        var host = RpcServerHostBuilder.Create()
            .UseSerializer(new JsonRpcSerializer())
            .UseAcceptor(_ => ValueTask.FromResult<IRpcConnectionAcceptor>(acceptor))
            .ConfigureServices(_ => { })
            .Build();

        var runTask = host.RunAsync(cts.Token).AsTask();
        await Task.Delay(50); // let RunAsync reach AcceptAsync

        cts.Cancel();

        // Must complete without throwing ObjectDisposedException
        var ex = await Record.ExceptionAsync(() => runTask.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Null(ex);

        // The inner acceptor must have been disposed exactly once
        Assert.Equal(1, acceptorDisposed);
    }

    [Fact]
    public async Task RunAsync_NotifiesSessionLifecycleObserverOncePerConnection()
    {
        var observer = new RecordingSessionLifecycleObserver();
        var acceptor = new SingleConnectionAcceptor(new EmptyFrameTransport(), "client-a");
        using var cts = new CancellationTokenSource();

        var host = RpcServerHostBuilder.Create()
            .UseSerializer(new JsonRpcSerializer())
            .UseAcceptor(_ => ValueTask.FromResult<IRpcConnectionAcceptor>(acceptor))
            .UseSessionLifecycleObserver(observer)
            .ConfigureServices(_ => { })
            .Build();

        var runTask = host.RunAsync(cts.Token).AsTask();

        await observer.Disconnected.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        var started = Assert.Single(observer.StartedContexts);
        var disconnected = Assert.Single(observer.DisconnectedContexts);
        Assert.Equal("client-a", started.ConnectionId);
        Assert.Equal("client-a", started.DisplayName);
        Assert.Equal(started.ConnectionId, disconnected.Context.ConnectionId);
        Assert.Null(disconnected.Error);
    }

    [Fact]
    public void BindFromAssembly_UsesAssemblyLevelGeneratedBinderAttribute()
    {
        var registry = new RpcServiceRegistry();

        RpcGeneratedServiceBinder.BindFromAssembly(typeof(TestGeneratedBinder).Assembly, registry);

        Assert.False(registry.IsEmpty);
        Assert.True(registry.TryGetHandler(7, 9, out _));
    }

    private sealed class NoopConnectionAcceptor : IRpcConnectionAcceptor
    {
        public string ListenAddress => "test://noop";

        public ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default)
        {
            return ValueTask.FromException<RpcAcceptedConnection>(new NotSupportedException());
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TrackingNeverAcceptAcceptor : IRpcConnectionAcceptor
    {
        private readonly Action _onDispose;

        public TrackingNeverAcceptAcceptor(Action onDispose) => _onDispose = onDispose;

        public string ListenAddress => "test://tracking";

        public async ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return default!;
        }

        public ValueTask DisposeAsync()
        {
            _onDispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingSessionLifecycleObserver : IRpcSessionLifecycleObserver
    {
        public List<RpcSessionLifecycleContext> StartedContexts { get; } = [];

        public List<(RpcSessionLifecycleContext Context, Exception? Error)> DisconnectedContexts { get; } = [];

        public TaskCompletionSource Disconnected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask OnSessionStartedAsync(
            RpcSessionLifecycleContext context,
            CancellationToken cancellationToken = default)
        {
            StartedContexts.Add(context);
            return default;
        }

        public ValueTask OnSessionDisconnectedAsync(
            RpcSessionLifecycleContext context,
            Exception? error,
            CancellationToken cancellationToken = default)
        {
            DisconnectedContexts.Add((context, error));
            Disconnected.TrySetResult();
            return default;
        }
    }

    private sealed class SingleConnectionAcceptor : IRpcConnectionAcceptor
    {
        private readonly RpcAcceptedConnection _connection;
        private int _accepted;

        public SingleConnectionAcceptor(ITransport transport, string displayName)
        {
            _connection = new RpcAcceptedConnection(transport, displayName);
        }

        public string ListenAddress => "test://single";

        public ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _accepted, 1) == 0)
                return ValueTask.FromResult(_connection);

            return ValueTask.FromCanceled<RpcAcceptedConnection>(ct.IsCancellationRequested ? ct : new CancellationToken(true));
        }

        public ValueTask DisposeAsync()
        {
            return default;
        }
    }

    private sealed class EmptyFrameTransport : ITransport
    {
        public bool IsConnected { get; private set; } = true;

        public ValueTask ConnectAsync(CancellationToken ct = default)
        {
            IsConnected = true;
            return default;
        }

        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            return default;
        }

        public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            IsConnected = false;
            return ValueTask.FromResult(TransportFrame.Empty);
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return default;
        }
    }
}

public static class TestGeneratedBinder
{
    public static void BindAll(RpcServiceRegistry registry)
    {
        registry.Register(7, 9, static (session, request, ct) => ValueTask.FromResult(
            RpcEnvelopeCodec.EncodeResponse(request.RequestId, RpcStatus.Ok, ReadOnlyMemory<byte>.Empty)));
    }
}
