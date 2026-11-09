using System.Collections.Concurrent;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Lakona.Rpc.Serializer.Json;
using Microsoft.Extensions.Logging;

namespace Lakona.Rpc.Tests;

public sealed class RpcServerCancellationTests
{
    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(true, true, 1)]
    [InlineData(false, false, 3)]
    [InlineData(true, false, 3)]
    public async Task Blocked_callbacks_cannot_prevent_shared_deadline_abort_or_late_cleanup(
        bool listenerFails, bool callbackThrows, int sessionCount)
    {
        await using var fixture = new Fixture(sessionCount, blockCallback: true, callbackThrows,
            TimeSpan.FromMilliseconds(250));
        await fixture.StartedAsync();

        // Do not run the caller's Cancel on the test thread: the old linked-token
        // path blocks it inside the business callback. The gate is released in cleanup.
        await fixture.TriggerStopAsync(listenerFails).WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(fixture.Services.Select(service => service.CallbackEntered.Task))
            .WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(fixture.Transports.Select(transport => transport.Disposed.Task))
            .WaitAsync(TimeSpan.FromSeconds(5));
        var error = await Record.ExceptionAsync(() => fixture.Run.WaitAsync(TimeSpan.FromSeconds(5)));
        if (listenerFails) Assert.Same(fixture.Acceptor.Error, error);
        else
        {
            var timeout = Assert.IsType<RpcServerShutdownTimeoutException>(error);
            Assert.Equal(sessionCount, timeout.ActiveSessionCount);
            Assert.Equal(TimeSpan.FromMilliseconds(250), timeout.ShutdownTimeout);
        }

        // Every transport is aborted while ALL callbacks remain blocked. Neither
        // cancellation nor cleanup gets a separate serial budget for each Session.
        Assert.All(fixture.Services, service =>
        {
            Assert.False(service.CallbackExited.Task.IsCompleted);
            Assert.Equal(0, service.DisposeCount);
        });
        Assert.All(fixture.Leases, lease => Assert.Equal(0, lease.DisposeCount));
        Assert.Equal(0, fixture.DisconnectedCount);

        fixture.ReleaseCallbacks();
        await Task.WhenAll(fixture.Services.Select(service => service.CallbackExited.Task))
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(fixture.Services, service => Assert.Equal(0, service.DisposeCount));
        fixture.ReleaseHandlers();
        await fixture.AllDisconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Logs.LateCleanup.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.AssertReleasedOnce();
        if (callbackThrows)
            Assert.Contains(fixture.Logs.Errors, exception => Contains(exception, fixture.Services[0].Error));
        if (listenerFails)
            Assert.Contains(fixture.Logs.Errors, exception => exception is RpcServerShutdownTimeoutException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Throwing_callback_is_observed_only_after_other_cleanup_completes(bool listenerFails)
    {
        await using var fixture = new Fixture(1, blockCallback: false, callbackThrows: true,
            TimeSpan.FromSeconds(10));
        await fixture.StartedAsync();
        await fixture.TriggerStopAsync(listenerFails).WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Services[0].CallbackExited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(fixture.Run.IsCompleted);
        Assert.Equal(0, fixture.Services[0].DisposeCount);
        Assert.Equal(0, fixture.Leases.Single().DisposeCount);

        fixture.ReleaseHandlers();
        var error = await Record.ExceptionAsync(() => fixture.Run.WaitAsync(TimeSpan.FromSeconds(5)));
        if (listenerFails)
        {
            Assert.Same(fixture.Acceptor.Error, error);
            Assert.Contains(fixture.Logs.Errors, exception => Contains(exception, fixture.Services[0].Error));
        }
        else Assert.True(Contains(error, fixture.Services[0].Error), error?.ToString());
        fixture.AssertReleasedOnce();
    }

    private static bool Contains(Exception? actual, Exception expected) =>
        ReferenceEquals(actual, expected) ||
        actual is AggregateException aggregate && aggregate.Flatten().InnerExceptions.Contains(expected);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_completion_is_joined_even_after_every_session_has_disconnected(bool listenerFails)
    {
        await using var fixture = new Fixture(1, blockCallback: false, callbackThrows: false,
            TimeSpan.FromMilliseconds(250), retainAdmissionCallback: true);
        await fixture.StartedAsync();
        fixture.ReleaseHandlers();
        // Finish the Session before stopping the listener. Only an admission
        // callback retained by this fixture remains to be cancelled and joined.
        await fixture.Transports[0].RequestSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Transports[0].ClosePeer();
        await fixture.AllDisconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.TriggerStopAsync(listenerFails).WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.AdmissionCallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var error = await Record.ExceptionAsync(() => fixture.Run.WaitAsync(TimeSpan.FromSeconds(5)));
        if (listenerFails) Assert.Same(fixture.Acceptor.Error, error);
        else Assert.Equal(0, Assert.IsType<RpcServerShutdownTimeoutException>(error).ActiveSessionCount);

        fixture.ReleaseAdmissionCallback.TrySetResult();
        await fixture.Logs.LateCleanup.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(fixture.Logs.Errors, exception => Contains(exception, fixture.AdmissionCallbackError));
        fixture.AssertReleasedOnce(expectedCallbackCount: 0);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Fixture : IAsyncDisposable, IRpcSessionAdmissionGate, IRpcSessionLifecycleObserver
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly ILoggerFactory _loggerFactory;
        private readonly bool _retainAdmissionCallback;
        private CancellationTokenRegistration _admissionRegistration;
        private int _disconnectedCount;
        public int DisconnectedCount => Volatile.Read(ref _disconnectedCount);
        public TaskCompletionSource AllDisconnected { get; } = Signal();
        public CallbackService[] Services { get; }
        public RequestTransport[] Transports { get; }
        public ConcurrentBag<Lease> Leases { get; } = [];
        public TestAcceptor Acceptor { get; }
        public LogProvider Logs { get; } = new();
        public TaskCompletionSource AdmissionCallbackEntered { get; } = Signal();
        public TaskCompletionSource ReleaseAdmissionCallback { get; } = Signal();
        public Exception AdmissionCallbackError { get; } = new InvalidOperationException("Late admission cancellation failure.");
        public Task Run { get; }

        public Fixture(int count, bool blockCallback, bool callbackThrows, TimeSpan timeout, bool retainAdmissionCallback = false)
        {
            _retainAdmissionCallback = retainAdmissionCallback;
            Services = Enumerable.Range(0, count).Select(_ => new CallbackService(blockCallback, callbackThrows)).ToArray();
            Transports = Enumerable.Range(0, count).Select(_ => new RequestTransport()).ToArray();
            Acceptor = new TestAcceptor(Transports);
            _loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(Logs));
            var serviceIndex = -1;
            var host = RpcServerHostBuilder.Create()
                .UseSerializer(new JsonRpcSerializer())
                .UseAcceptor(Acceptor)
                .UseShutdownTimeout(timeout)
                .UseLoggerFactory(_loggerFactory)
                .UseSessionAdmissionGate(this)
                .UseSessionLifecycleObserver(this)
                .ConfigureServices(registry => registry
                    .RegisterPerConnection(1, (_, _) => Services[Interlocked.Increment(ref serviceIndex)])
                    .Register<int>(1, static (service, _, ct) => service.HandleAsync(ct)))
                .Build();
            Run = host.RunAsync(_stop.Token).AsTask();
        }

        public Task StartedAsync() => Task.WhenAll(Services.Select(service => service.Started.Task))
            .WaitAsync(TimeSpan.FromSeconds(10));

        public Task TriggerStopAsync(bool listenerFails)
        {
            if (!listenerFails) return Task.Run(() => _stop.Cancel());
            Acceptor.Fail.TrySetResult();
            return Task.CompletedTask;
        }

        public void ReleaseCallbacks() { foreach (var service in Services) service.ReleaseCallback.TrySetResult(); }
        public void ReleaseHandlers() { foreach (var service in Services) service.ReleaseHandler.TrySetResult(); }

        public void AssertReleasedOnce(int expectedCallbackCount = 1)
        {
            Assert.Equal(1, Acceptor.DisposeCount);
            Assert.Equal(Services.Length, DisconnectedCount);
            Assert.Equal(Services.Length, Leases.Count);
            Assert.All(Transports, transport => Assert.Equal(1, transport.DisposeCount));
            Assert.All(Leases, lease => Assert.Equal(1, lease.DisposeCount));
            Assert.All(Services, service =>
            {
                Assert.Equal(expectedCallbackCount, service.CallbackCount);
                Assert.Equal(1, service.DisposeCount);
                Assert.False(service.DisposedWhileRunning);
            });
        }

        public ValueTask<RpcSessionAdmissionResult> EvaluateAsync(RpcSessionAdmissionContext context, CancellationToken cancellationToken)
        {
            if (_retainAdmissionCallback)
                _admissionRegistration = cancellationToken.Register(() =>
                {
                    AdmissionCallbackEntered.TrySetResult();
                    ReleaseAdmissionCallback.Task.GetAwaiter().GetResult();
                    throw AdmissionCallbackError;
                });
            var lease = new Lease();
            Leases.Add(lease);
            return new ValueTask<RpcSessionAdmissionResult>(RpcSessionAdmissionResult.Allow(lease: lease));
        }

        public ValueTask OnSessionStartedAsync(RpcSessionLifecycleContext context, CancellationToken cancellationToken) => default;
        public ValueTask OnSessionDisconnectedAsync(RpcSessionLifecycleContext context, Exception? error, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _disconnectedCount) == Services.Length) AllDisconnected.TrySetResult();
            return default;
        }

        public async ValueTask DisposeAsync()
        {
            ReleaseAdmissionCallback.TrySetResult();
            ReleaseCallbacks();
            ReleaseHandlers();
            await Record.ExceptionAsync(() => Task.Run(() => _stop.Cancel()).WaitAsync(TimeSpan.FromSeconds(10)));
            await Record.ExceptionAsync(() => Run.WaitAsync(TimeSpan.FromSeconds(10)));
            await AllDisconnected.Task.WaitAsync(TimeSpan.FromSeconds(10));
            _admissionRegistration.Dispose();
            _stop.Dispose();
            _loggerFactory.Dispose();
        }
    }

    private sealed class CallbackService(bool blockCallback, bool callbackThrows) : IAsyncDisposable
    {
        private int _running;
        public int CallbackCount;
        public int DisposeCount;
        public bool DisposedWhileRunning;
        public Exception Error { get; } = new InvalidOperationException("Injected cancellation callback failure.");
        public TaskCompletionSource Started { get; } = Signal();
        public TaskCompletionSource CallbackEntered { get; } = Signal();
        public TaskCompletionSource CallbackExited { get; } = Signal();
        public TaskCompletionSource ReleaseCallback { get; } = Signal();
        public TaskCompletionSource ReleaseHandler { get; } = Signal();

        public async ValueTask HandleAsync(CancellationToken ct)
        {
            Interlocked.Exchange(ref _running, 1);
            using var registration = ct.Register(() =>
            {
                Interlocked.Increment(ref CallbackCount);
                CallbackEntered.TrySetResult();
                try
                {
                    if (blockCallback) ReleaseCallback.Task.GetAwaiter().GetResult();
                    if (callbackThrows) throw Error;
                }
                finally { CallbackExited.TrySetResult(); }
            });
            Started.TrySetResult();
            await ReleaseHandler.Task;
            Interlocked.Exchange(ref _running, 0);
        }

        public ValueTask DisposeAsync()
        {
            DisposedWhileRunning = Volatile.Read(ref _running) != 0;
            Interlocked.Increment(ref DisposeCount);
            return default;
        }
    }

    private sealed class Lease : IAsyncDisposable
    {
        public int DisposeCount;
        public ValueTask DisposeAsync() { Interlocked.Increment(ref DisposeCount); return default; }
    }

    private sealed class TestAcceptor(RequestTransport[] transports) : IRpcConnectionAcceptor
    {
        private int _next;
        public int DisposeCount;
        public IOException Error { get; } = new("Injected listener failure.");
        public TaskCompletionSource Fail { get; } = Signal();
        public string ListenAddress => "test://cancellation";
        public async ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct)
        {
            if (_next < transports.Length) return new RpcAcceptedConnection(transports[_next++], "callback");
            await Fail.Task.WaitAsync(ct);
            throw Error;
        }
        public ValueTask DisposeAsync() { Interlocked.Increment(ref DisposeCount); return default; }
    }

    private sealed class RequestTransport : ITransport
    {
        private int _reads;
        public int DisposeCount;
        public TaskCompletionSource Disposed { get; } = Signal();
        public TaskCompletionSource RequestSent { get; } = Signal();
        private readonly TaskCompletionSource _peerClosed = Signal();
        public bool IsConnected => Volatile.Read(ref DisposeCount) == 0;
        public ValueTask ConnectAsync(CancellationToken ct = default) => default;
        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            RequestSent.TrySetResult();
            return default;
        }
        public void ClosePeer() => _peerClosed.TrySetResult();
        public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _reads) == 1)
                return RpcEnvelopeCodec.EncodeRequest(new RpcRequestEnvelope
                { RequestId = 1, ServiceId = 1, MethodId = 1, Payload = "0"u8.ToArray() });
            await _peerClosed.Task.WaitAsync(ct);
            return TransportFrame.Empty;
        }
        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref DisposeCount);
            ClosePeer();
            Disposed.TrySetResult();
            return default;
        }
    }

    private sealed class LogProvider : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<Exception> Errors { get; } = new();
        public TaskCompletionSource LateCleanup { get; } = Signal();
        public ILogger CreateLogger(string categoryName) => this;
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        {
            if (error is not null) Errors.Enqueue(error);
            if (formatter(state, error).Contains("Session cleanup completed after its shutdown deadline", StringComparison.Ordinal))
                LateCleanup.TrySetResult();
        }
    }
}
