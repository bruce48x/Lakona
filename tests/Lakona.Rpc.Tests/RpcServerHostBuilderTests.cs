using Microsoft.Extensions.Logging;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Transport.Loopback;

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
                limits.MaxActiveConnections = 24;
            });

        Assert.Equal(8, builder.Limits.MaxConcurrentRequestsPerSession);
        Assert.Equal(32, builder.Limits.MaxQueuedRequestsPerSession);
        Assert.Equal(12, builder.Limits.MaxPendingAcceptedConnections);
        Assert.Equal(24, builder.Limits.MaxActiveConnections);
    }

    [Fact]
    public void PublicLoggingSeam_ExposesOnlyLoggerFactory()
    {
        var publicMethods = typeof(RpcServerHostBuilder).GetMethods();

        Assert.DoesNotContain(publicMethods, method => method.Name == "UseLogger");
        Assert.Single(publicMethods, method => method.Name == nameof(RpcServerHostBuilder.UseLoggerFactory));
    }

    [Fact]
    public async Task RunAsync_RejectsConnectionBeyondActiveLimitBeforeStartingSession()
    {
        var firstTransport = new BlockingTransport();
        var rejectedTransport = new BlockingTransport();
        var observer = new RecordingSessionLifecycleObserver();
        var acceptor = new QueueConnectionAcceptor(
            new RpcAcceptedConnection(firstTransport, "first"),
            new RpcAcceptedConnection(rejectedTransport, "rejected"));
        using var cts = new CancellationTokenSource();

        var host = RpcServerHostBuilder.Create()
            .UseSerializer(new JsonRpcSerializer())
            .UseAcceptor(_ => new ValueTask<IRpcConnectionAcceptor>(acceptor))
            .UseLimits(limits => limits.MaxActiveConnections = 1)
            .UseSessionLifecycleObserver(observer)
            .ConfigureServices(_ => { })
            .Build();

        var runTask = host.RunAsync(cts.Token).AsTask();

        await firstTransport.ReceiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await rejectedTransport.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Single(observer.StartedContexts);
        Assert.Equal("first", observer.StartedContexts[0].DisplayName);

        cts.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RunAsync_ReleasesActiveConnectionSlotBeforeDisconnectedObserver()
    {
        var firstTransport = new BlockingTransport();
        var secondTransport = new BlockingTransport();
        var acceptor = new ChannelConnectionAcceptor();
        var firstDisconnectCompleted = new TaskCompletionSource<(bool TransportDisposed, bool SecondAdmitted)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new RecordingSessionLifecycleObserver(async (context, _) =>
        {
            if (context.DisplayName != "first")
                return;

            var transportDisposed = firstTransport.Disposed.Task.IsCompleted;
            acceptor.Enqueue(new RpcAcceptedConnection(secondTransport, "second"));
            var secondOutcome = await Task.WhenAny(
                secondTransport.ReceiveStarted.Task,
                secondTransport.Disposed.Task).WaitAsync(TimeSpan.FromSeconds(2));
            firstDisconnectCompleted.TrySetResult((
                transportDisposed,
                secondOutcome == secondTransport.ReceiveStarted.Task));
        });
        using var cts = new CancellationTokenSource();
        var host = RpcServerHostBuilder.Create()
            .UseSerializer(new JsonRpcSerializer())
            .UseAcceptor(_ => new ValueTask<IRpcConnectionAcceptor>(acceptor))
            .UseLimits(limits => limits.MaxActiveConnections = 1)
            .UseSessionLifecycleObserver(observer)
            .ConfigureServices(_ => { })
            .Build();
        var runTask = host.RunAsync(cts.Token).AsTask();

        acceptor.Enqueue(new RpcAcceptedConnection(firstTransport, "first"));
        await firstTransport.ReceiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        firstTransport.CompleteReceive();
        var firstDisconnect = await firstDisconnectCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(firstDisconnect.TransportDisposed);
        Assert.True(firstDisconnect.SecondAdmitted);
        Assert.Equal(2, observer.StartedContexts.Count);

        cts.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
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
            .UseAcceptor(_ => new ValueTask<IRpcConnectionAcceptor>(acceptor))
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
    public async Task RunAsync_LogsListeningAddressWithoutShutdownPrompt()
    {
        var loggerProvider = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(loggerProvider));
        using var cts = new CancellationTokenSource();
        var acceptor = new TrackingNeverAcceptAcceptor(() => { });

        var host = RpcServerHostBuilder.Create()
            .UseSerializer(new JsonRpcSerializer())
            .UseAcceptor(_ => new ValueTask<IRpcConnectionAcceptor>(acceptor))
            .UseLoggerFactory(loggerFactory)
            .ConfigureServices(_ => { })
            .Build();

        var runTask = host.RunAsync(cts.Token).AsTask();
        var entry = await loggerProvider.WaitForEntryAsync(
            candidate => candidate.Message.StartsWith("RPC server listening on ", StringComparison.Ordinal),
            cts.Token).WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(typeof(RpcServerHost).FullName, entry.Category);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("RPC server listening on test://tracking.", entry.Message);
    }

    [Fact]
    public async Task RunAsync_LoggerFactoryCreatesDedicatedRequestCategory()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var loggerProvider = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(loggerProvider);
        });
        using var cts = new CancellationTokenSource();
        var host = RpcServerHostBuilder.Create()
            .UseSerializer(new JsonRpcSerializer())
            .UseAcceptor(new HoldingSingleConnectionAcceptor(serverTransport, "logging-client"))
            .UseLoggerFactory(loggerFactory)
            .ConfigureServices(registry => registry.RegisterRaw(
                1,
                1,
                static (_, _, _, _) => new ValueTask<RpcRawResult>(
                    RpcRawResult.Ok(ReadOnlyMemory<byte>.Empty))))
            .Build();

        await clientTransport.ConnectAsync();
        await serverTransport.ConnectAsync();
        var runTask = host.RunAsync(cts.Token).AsTask();

        try
        {
            await SendRequestAsync(clientTransport, 1);
            var entry = await loggerProvider.WaitForEntryAsync(
                candidate => candidate.Message.Contains("RPC request received", StringComparison.Ordinal),
                cts.Token).WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal("Lakona.Rpc.Server.Request", entry.Category);
            Assert.Equal(LogLevel.Trace, entry.Level);
        }
        finally
        {
            cts.Cancel();
            await clientTransport.DisposeAsync();
            await runTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
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
        Assert.NotEmpty(started.ConnectionId);
        Assert.NotEqual("client-a", started.ConnectionId);
        Assert.Equal("client-a", started.DisplayName);
        Assert.Equal(started.ConnectionId, disconnected.Context.ConnectionId);
        Assert.Null(disconnected.Error);
    }

    [Fact]
    public async Task RunAsync_WhenOverloadResponseIsBlocked_StopsReadingRequestsUntilSendCompletes()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var blockingServerTransport = new BlockingSendTransport(serverTransport);
        var firstRequestStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstRequest = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();

        var host = RpcServerHostBuilder.Create()
            .UseSerializer(new JsonRpcSerializer())
            .UseAcceptor(new HoldingSingleConnectionAcceptor(blockingServerTransport, "overload-client"))
            .UseLimits(limits =>
            {
                limits.MaxConcurrentRequestsPerSession = 1;
                limits.MaxQueuedRequestsPerSession = 0;
            })
            .ConfigureServices(registry => registry.RegisterRaw(
                1,
                1,
                async (_, _, _, cancellationToken) =>
                {
                    firstRequestStarted.TrySetResult();
                    await releaseFirstRequest.Task.WaitAsync(cancellationToken);
                    return RpcRawResult.Ok(ReadOnlyMemory<byte>.Empty);
                }))
            .Build();

        await clientTransport.ConnectAsync();
        await blockingServerTransport.ConnectAsync();
        var runTask = host.RunAsync(cts.Token).AsTask();

        try
        {
            await SendRequestAsync(clientTransport, 1);
            await firstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            await SendRequestAsync(clientTransport, 2);
            await blockingServerTransport.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            await SendRequestAsync(clientTransport, 3);

            await Assert.ThrowsAsync<TimeoutException>(() =>
                blockingServerTransport.ThirdReceiveStarted.Task.WaitAsync(TimeSpan.FromMilliseconds(500)));
        }
        finally
        {
            blockingServerTransport.ReleaseSend();
            releaseFirstRequest.TrySetResult();
            cts.Cancel();
            await clientTransport.DisposeAsync();
            await runTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task RunAsync_GeneratesUniqueConnectionIdsForMatchingDisplayNames()
    {
        var observer = new TwoConnectionLifecycleObserver();
        var acceptor = new QueueConnectionAcceptor(
            new RpcAcceptedConnection(new EmptyFrameTransport(), "shared-display-name"),
            new RpcAcceptedConnection(new EmptyFrameTransport(), "shared-display-name"));
        using var cts = new CancellationTokenSource();

        var host = RpcServerHostBuilder.Create()
            .UseSerializer(new JsonRpcSerializer())
            .UseAcceptor(_ => new ValueTask<IRpcConnectionAcceptor>(acceptor))
            .UseSessionLifecycleObserver(observer)
            .ConfigureServices(_ => { })
            .Build();

        var runTask = host.RunAsync(cts.Token).AsTask();

        await observer.AllDisconnected.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        var started = observer.StartedContexts.ToArray();
        Assert.Equal(2, started.Length);
        Assert.All(started, context => Assert.Equal("shared-display-name", context.DisplayName));
        Assert.Equal(2, started.Select(context => context.ConnectionId).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(started, context => context.ConnectionId == context.DisplayName);
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
        private readonly Func<RpcSessionLifecycleContext, Exception?, ValueTask>? _onDisconnected;

        public RecordingSessionLifecycleObserver(
            Func<RpcSessionLifecycleContext, Exception?, ValueTask>? onDisconnected = null)
        {
            _onDisconnected = onDisconnected;
        }

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
            return _onDisconnected is null
                ? default
                : _onDisconnected(context, error);
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

    private sealed class QueueConnectionAcceptor(params RpcAcceptedConnection[] connections) : IRpcConnectionAcceptor
    {
        private readonly Queue<RpcAcceptedConnection> _connections = new(connections);

        public string ListenAddress => "test://queue";

        public ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default)
        {
            if (_connections.Count > 0)
                return new ValueTask<RpcAcceptedConnection>(_connections.Dequeue());

            return ValueTask.FromCanceled<RpcAcceptedConnection>(
                ct.IsCancellationRequested ? ct : new CancellationToken(true));
        }

        public ValueTask DisposeAsync()
        {
            return default;
        }
    }

    private sealed class HoldingSingleConnectionAcceptor : IRpcConnectionAcceptor
    {
        private readonly RpcAcceptedConnection _connection;
        private int _accepted;

        public HoldingSingleConnectionAcceptor(ITransport transport, string displayName)
        {
            _connection = new RpcAcceptedConnection(transport, displayName);
        }

        public string ListenAddress => "test://holding-single";

        public async ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _accepted, 1) == 0)
                return _connection;

            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return default!;
        }

        public ValueTask DisposeAsync()
        {
            return default;
        }
    }

    private sealed class ChannelConnectionAcceptor : IRpcConnectionAcceptor
    {
        private readonly System.Threading.Channels.Channel<RpcAcceptedConnection> _connections =
            System.Threading.Channels.Channel.CreateUnbounded<RpcAcceptedConnection>();

        public string ListenAddress => "test://channel";

        public void Enqueue(RpcAcceptedConnection connection)
        {
            Assert.True(_connections.Writer.TryWrite(connection));
        }

        public ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default)
        {
            return _connections.Reader.ReadAsync(ct);
        }

        public ValueTask DisposeAsync()
        {
            _connections.Writer.TryComplete();
            return default;
        }
    }

    private sealed class TwoConnectionLifecycleObserver : IRpcSessionLifecycleObserver
    {
        private int _disconnected;

        public System.Collections.Concurrent.ConcurrentQueue<RpcSessionLifecycleContext> StartedContexts { get; } = new();

        public TaskCompletionSource AllDisconnected { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask OnSessionStartedAsync(
            RpcSessionLifecycleContext context,
            CancellationToken cancellationToken = default)
        {
            StartedContexts.Enqueue(context);
            return default;
        }

        public ValueTask OnSessionDisconnectedAsync(
            RpcSessionLifecycleContext context,
            Exception? error,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _disconnected) == 2)
                AllDisconnected.TrySetResult();

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

    private sealed class BlockingTransport : ITransport
    {
        private readonly TaskCompletionSource<TransportFrame> _receiveCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsConnected { get; private set; } = true;

        public TaskCompletionSource ReceiveStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Disposed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask ConnectAsync(CancellationToken ct = default)
        {
            IsConnected = true;
            return default;
        }

        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            return default;
        }

        public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            ReceiveStarted.TrySetResult();
            return await _receiveCompletion.Task.WaitAsync(ct);
        }

        public void CompleteReceive()
        {
            _receiveCompletion.TrySetResult(TransportFrame.Empty);
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            Disposed.TrySetResult();
            return default;
        }
    }

    private sealed class BlockingSendTransport(ITransport inner) : ITransport
    {
        private readonly TaskCompletionSource _releaseSend =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _receiveCount;

        public bool IsConnected => inner.IsConnected;

        public TaskCompletionSource SendStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ThirdReceiveStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask ConnectAsync(CancellationToken ct = default)
        {
            return inner.ConnectAsync(ct);
        }

        public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            SendStarted.TrySetResult();
            await _releaseSend.Task.WaitAsync(ct);
            await inner.SendFrameAsync(frame, ct);
        }

        public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _receiveCount) == 3)
                ThirdReceiveStarted.TrySetResult();

            return inner.ReceiveFrameAsync(ct);
        }

        public void ReleaseSend()
        {
            _releaseSend.TrySetResult();
        }

        public ValueTask DisposeAsync()
        {
            return inner.DisposeAsync();
        }
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly System.Threading.Channels.Channel<LogEntry> _entries =
            System.Threading.Channels.Channel.CreateUnbounded<LogEntry>();

        public ILogger CreateLogger(string categoryName)
        {
            return new RecordingLogger(categoryName, _entries.Writer);
        }

        public async Task<LogEntry> WaitForEntryAsync(
            Func<LogEntry, bool> predicate,
            CancellationToken cancellationToken)
        {
            while (await _entries.Reader.WaitToReadAsync(cancellationToken))
            {
                while (_entries.Reader.TryRead(out var entry))
                {
                    if (predicate(entry))
                        return entry;
                }
            }

            throw new InvalidOperationException("The logger provider completed before a matching entry was written.");
        }

        public void Dispose()
        {
            _entries.Writer.TryComplete();
        }

        public sealed record LogEntry(string Category, LogLevel Level, string Message);

        private sealed class RecordingLogger(
            string category,
            System.Threading.Channels.ChannelWriter<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                entries.TryWrite(new LogEntry(category, logLevel, formatter(state, exception)));
            }
        }
    }

    private static async ValueTask SendRequestAsync(ITransport transport, uint requestId)
    {
        using var frame = RpcEnvelopeCodec.EncodeRequest(new RpcRequestEnvelope
        {
            RequestId = requestId,
            ServiceId = 1,
            MethodId = 1,
            Payload = ReadOnlyMemory<byte>.Empty
        });
        await transport.SendFrameAsync(frame.Memory);
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
