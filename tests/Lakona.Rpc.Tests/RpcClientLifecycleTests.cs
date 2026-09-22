using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;

namespace Lakona.Rpc.Tests;

public sealed class RpcClientLifecycleTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposeDuringConnect_CancelsAndJoinsStartupBeforeReleasingTransport(bool observeCancellation)
    {
        var transport = new PausedConnectTransport(observeCancellation);
        var client = new RpcClientRuntime(transport, new JsonRpcSerializer());
        var starting = client.StartAsync().AsTask();
        await transport.Connecting.Task.WaitAsync(Deadline);
        var firstDisposal = client.DisposeAsync().AsTask();
        var secondDisposal = client.DisposeAsync().AsTask();
        try
        {
            await transport.Canceled.Task.WaitAsync(Deadline);
            Assert.False(firstDisposal.IsCompleted);
            Assert.False(secondDisposal.IsCompleted);
            Assert.Equal(0, transport.DisposeCount);
        }
        finally { transport.Release.TrySetResult(); }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting.WaitAsync(Deadline));
        await Task.WhenAll(firstDisposal, secondDisposal).WaitAsync(Deadline);
        Assert.False(transport.IsConnected);
        Assert.Equal(1, transport.DisposeCount);
        Assert.Equal(0, transport.ReceiveCount);
    }

    [Fact]
    public async Task KeepAliveWriteFailure_StopsReceiveAndFailsPendingCallsWithOriginalReason()
    {
        var transport = new FailingKeepAliveTransport();
        await using var client = new RpcClientRuntime(transport, new JsonRpcSerializer(), new RpcKeepAliveOptions
        {
            Enabled = true, Interval = TimeSpan.FromMilliseconds(10), Timeout = TimeSpan.FromSeconds(30)
        });
        var disconnected = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnectCount = 0;
        client.Disconnected += error => { Interlocked.Increment(ref disconnectCount); disconnected.TrySetResult(error); };
        await client.StartAsync();
        var pending = client.CallAsync(new RpcMethod<int, int>(1, 1), 42).AsTask();
        await transport.RequestSent.Task.WaitAsync(Deadline);
        await transport.PingStarted.Task.WaitAsync(Deadline);
        transport.FailPing.TrySetResult();

        Assert.Same(transport.Failure, await disconnected.Task.WaitAsync(Deadline));
        Assert.True(transport.ReceiveCanceled.Task.IsCompleted);
        Assert.Same(transport.Failure, await Assert.ThrowsAsync<IOException>(() => pending.WaitAsync(Deadline)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CallAsync(new RpcMethod<int, int>(1, 1), 0).AsTask());
        await client.DisposeAsync();
        Assert.Equal(1, disconnectCount);
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task ReceiveFailure_CancelsAnOutstandingKeepAliveWriteBeforeDisconnected()
    {
        var transport = new FailingKeepAliveTransport { FailReceive = true };
        await using var client = new RpcClientRuntime(transport, new JsonRpcSerializer(), new RpcKeepAliveOptions
        {
            Enabled = true, Interval = TimeSpan.FromMilliseconds(10), Timeout = TimeSpan.FromSeconds(30)
        });
        var disconnected = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += error => disconnected.TrySetResult(error);
        await client.StartAsync();
        await transport.PingStarted.Task.WaitAsync(Deadline);
        transport.ReleaseReceive.TrySetResult();
        Assert.Same(transport.Failure, await disconnected.Task.WaitAsync(Deadline));
        Assert.True(transport.PingCanceled.Task.IsCompleted);
    }

    [Fact]
    public async Task ConcurrentDisposals_WaitForTheSameTransportCleanup()
    {
        var transport = new PausedDisposeTransport();
        var client = new RpcClientRuntime(transport, new JsonRpcSerializer());
        var first = client.DisposeAsync().AsTask();
        await transport.Disposing.Task.WaitAsync(Deadline);
        var second = client.DisposeAsync().AsTask();
        try { Assert.False(second.IsCompleted); }
        finally { transport.Release.TrySetResult(); }
        await Task.WhenAll(first, second).WaitAsync(Deadline);
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task ReceiveFailure_DrainsAlreadyReceivedResponsesBeforeDisconnecting()
    {
        var transport = new ResponseThenCloseTransport();
        await using var client = new RpcClientRuntime(transport, new JsonRpcSerializer());
        var context = new PausedContext();
        client.SetDispatchSynchronizationContext(context);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += _ => disconnected.TrySetResult();
        await client.StartAsync();
        var pending = client.CallAsync(new RpcMethod<int, int>(1, 1), 42).AsTask();
        await transport.Closed.Task.WaitAsync(Deadline);
        await context.Posted.Task.WaitAsync(Deadline);
        Assert.False(pending.IsCompleted);
        context.Run();
        Assert.Equal(42, await pending.WaitAsync(Deadline));
        await disconnected.Task.WaitAsync(Deadline);
    }

    private sealed class PausedConnectTransport(bool observeCancellation) : ITransport
    {
        public TaskCompletionSource Connecting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposeCount;
        public int ReceiveCount;
        public bool IsConnected { get; private set; }
        public async ValueTask ConnectAsync(CancellationToken ct = default)
        {
            using var registration = ct.Register(() => Canceled.TrySetResult());
            Connecting.TrySetResult();
            await Release.Task;
            if (observeCancellation) ct.ThrowIfCancellationRequested();
            IsConnected = true;
        }
        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default) => default;
        public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref ReceiveCount);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return TransportFrame.Empty;
        }
        public ValueTask DisposeAsync() { DisposeCount++; IsConnected = false; return default; }
    }

    private sealed class FailingKeepAliveTransport : ITransport
    {
        public IOException Failure { get; } = new("Injected connection failure.");
        public TaskCompletionSource RequestSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PingStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FailPing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReceiveCanceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PingCanceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseReceive { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FailReceive;
        public int DisposeCount;
        public bool IsConnected { get; private set; }
        public ValueTask ConnectAsync(CancellationToken ct = default) { IsConnected = true; return default; }
        public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            if (RpcEnvelopeCodec.PeekFrameType(frame.Span) != RpcFrameType.KeepAlivePing)
            {
                RequestSent.TrySetResult();
                return;
            }
            if (!FailReceive && !RequestSent.Task.IsCompleted) return;
            PingStarted.TrySetResult();
            try { await FailPing.Task.WaitAsync(ct); }
            catch (OperationCanceledException) { PingCanceled.TrySetResult(); throw; }
            throw Failure;
        }
        public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            try { await ReleaseReceive.Task.WaitAsync(ct); }
            catch (OperationCanceledException) { ReceiveCanceled.TrySetResult(); throw; }
            if (FailReceive) throw Failure;
            return TransportFrame.Empty;
        }
        public ValueTask DisposeAsync() { DisposeCount++; IsConnected = false; return default; }
    }

    private sealed class PausedDisposeTransport : ITransport
    {
        public TaskCompletionSource Disposing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposeCount;
        public bool IsConnected => false;
        public ValueTask ConnectAsync(CancellationToken ct = default) => default;
        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default) => default;
        public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default) => new(TransportFrame.Empty);
        public async ValueTask DisposeAsync() { DisposeCount++; Disposing.TrySetResult(); await Release.Task; }
    }

    private sealed class ResponseThenCloseTransport : ITransport
    {
        private readonly TaskCompletionSource<uint> _request = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _responded;
        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsConnected => true;
        public ValueTask ConnectAsync(CancellationToken ct = default) => default;
        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            using var bytes = TransportFrame.CopyOf(frame.Span);
            using var request = RpcEnvelopeCodec.DecodeRequest(bytes);
            _request.TrySetResult(request.RequestId);
            return default;
        }
        public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            var id = await _request.Task.WaitAsync(ct);
            if (_responded) { Closed.TrySetResult(); return TransportFrame.Empty; }
            _responded = true;
            return RpcEnvelopeCodec.EncodeResponse(id, RpcStatus.Ok, "42"u8.ToArray());
        }
        public ValueTask DisposeAsync() => default;
    }

    private sealed class PausedContext : SynchronizationContext
    {
        private SendOrPostCallback? _callback;
        private object? _state;
        public TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void Post(SendOrPostCallback callback, object? state)
        { _callback = callback; _state = state; Posted.TrySetResult(); }
        public void Run() => _callback!(_state);
    }
}
