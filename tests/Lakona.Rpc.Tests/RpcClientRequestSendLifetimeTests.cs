using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;

namespace Lakona.Rpc.Tests;

public sealed class RpcClientRequestSendLifetimeTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectionEnd_CancelsAndJoinsRequestWritesBeforeDisconnectOrTransportDisposal(bool dispose)
    {
        var transport = new PausedWriteTransport();
        await using var client = new RpcClientRuntime(transport, new JsonRpcSerializer());
        var disconnected = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += error => disconnected.TrySetResult(error);
        await client.StartAsync();
        var first = client.CallAsync(new RpcMethod<int, int>(1, 1), 1).AsTask();
        await transport.Writing.Task.WaitAsync(Deadline);
        var queued = client.CallRawAsync(1, 2, "2"u8.ToArray()).AsTask();
        Task? disposal = null;
        try
        {
            if (dispose) disposal = client.DisposeAsync().AsTask();
            else transport.EndReceive.TrySetResult();
            await transport.WriteCanceled.Task.WaitAsync(Deadline);
            Assert.False(disconnected.Task.IsCompleted);
            Assert.False(disposal?.IsCompleted ?? false);
            Assert.Equal(0, transport.DisposeCount);
            Assert.Equal(1, transport.SendCount);
        }
        finally { transport.ReleaseWrite.TrySetResult(); }

        try
        {
            var error = await disconnected.Task.WaitAsync(Deadline);
            Assert.True(transport.WriteFinished);
            if (dispose)
            {
                await disposal!.WaitAsync(Deadline);
                await Assert.ThrowsAsync<ObjectDisposedException>(() => first);
                await Assert.ThrowsAsync<ObjectDisposedException>(() => queued);
            }
            else
            {
                Assert.Same(transport.ReceiveFailure, error);
                Assert.Same(error, await Assert.ThrowsAsync<IOException>(() => first));
                Assert.Same(error, await Assert.ThrowsAsync<IOException>(() => queued));
            }
            Assert.Equal(1, transport.SendCount);
        }
        finally { await client.DisposeAsync().AsTask().WaitAsync(Deadline); }
        Assert.False(transport.DisposedDuringWrite);
    }

    [Fact]
    public async Task CallerCancellation_CancelsWriteWithoutEndingTheConnection()
    {
        var transport = new PausedWriteTransport();
        await using var client = new RpcClientRuntime(transport, new JsonRpcSerializer());
        var disconnected = false;
        client.Disconnected += _ => disconnected = true;
        await client.StartAsync();
        using var cancellation = new CancellationTokenSource();
        var call = client.CallAsync(new RpcMethod<int, int>(1, 1), 1, cancellation.Token).AsTask();
        await transport.Writing.Task.WaitAsync(Deadline);
        try
        {
            cancellation.Cancel();
            await transport.WriteCanceled.Task.WaitAsync(Deadline);
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(Deadline));
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.False(disconnected);
        }
        finally { transport.ReleaseWrite.TrySetResult(); }
    }

    [Fact]
    public async Task ReceivedResponse_WinsOverConnectionCancellationOfItsOutstandingWrite()
    {
        var transport = new PausedWriteTransport { RespondBeforeFailure = true };
        await using var client = new RpcClientRuntime(transport, new JsonRpcSerializer());
        var context = new PausedContext();
        client.SetDispatchSynchronizationContext(context);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += _ => disconnected.TrySetResult();
        await client.StartAsync();
        var call = client.CallAsync(new RpcMethod<int, int>(1, 1), 1).AsTask();
        try
        {
            await context.Posted.Task.WaitAsync(Deadline);
            transport.EndReceive.TrySetResult();
            await transport.WriteCanceled.Task.WaitAsync(Deadline);
            Assert.False(call.IsCompleted);
            context.Run();
            Assert.Equal(42, await call.WaitAsync(Deadline));
            Assert.False(disconnected.Task.IsCompleted);
        }
        finally { transport.ReleaseWrite.TrySetResult(); }
        await disconnected.Task.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Dispose_JoinsAnAdmittedCallStillEncodingAndNeverSendsItsFrame()
    {
        var transport = new PausedWriteTransport();
        await using var client = new RpcClientRuntime(transport, new JsonRpcSerializer());
        await client.StartAsync();
        var encoding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var call = Task.Run(async () => await client.CallRawAsync(1, 1, writer =>
        {
            encoding.TrySetResult();
            Assert.True(release.Wait(Deadline));
            writer.GetSpan(1)[0] = 1;
            writer.Advance(1);
        }));
        Task disposal;
        try
        {
            await encoding.Task.WaitAsync(Deadline);
            disposal = client.DisposeAsync().AsTask();
            await transport.ReceiveCanceled.Task.WaitAsync(Deadline);
            Assert.False(disposal.IsCompleted);
            Assert.Equal(0, transport.DisposeCount);
        }
        finally { release.Set(); }
        await Assert.ThrowsAsync<ObjectDisposedException>(() => call.WaitAsync(Deadline));
        await disposal.WaitAsync(Deadline);
        Assert.Equal(0, transport.SendCount);
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

    private sealed class PausedWriteTransport : ITransport
    {
        public TaskCompletionSource Writing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource WriteCanceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource EndReceive { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReceiveCanceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IOException ReceiveFailure { get; } = new("Injected receive failure.");
        public bool RespondBeforeFailure;
        private bool _responded;
        private uint _requestId;
        public bool WriteFinished;
        public bool DisposedDuringWrite;
        public int DisposeCount;
        public int SendCount;
        public bool IsConnected { get; private set; }
        public ValueTask ConnectAsync(CancellationToken ct = default) { IsConnected = true; return default; }
        public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            Interlocked.Increment(ref SendCount);
            using (var bytes = TransportFrame.CopyOf(frame.Span))
            using (var request = RpcEnvelopeCodec.DecodeRequest(bytes))
                _requestId = request.RequestId;
            Writing.TrySetResult();
            using var canceled = ct.Register(() => WriteCanceled.TrySetResult());
            try
            {
                await ReleaseWrite.Task;
                // The borrowed frame must remain valid until this operation actually ends.
                Assert.Equal((byte)RpcFrameType.Request, frame.Span[0]);
                ct.ThrowIfCancellationRequested();
            }
            finally { WriteFinished = true; }
        }
        public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            if (RespondBeforeFailure && !_responded)
            {
                await Writing.Task.WaitAsync(ct);
                _responded = true;
                return RpcEnvelopeCodec.EncodeResponse(_requestId, RpcStatus.Ok, "42"u8.ToArray());
            }
            try { await EndReceive.Task.WaitAsync(ct); }
            catch (OperationCanceledException) { ReceiveCanceled.TrySetResult(); throw; }
            throw ReceiveFailure;
        }
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            DisposedDuringWrite = Writing.Task.IsCompleted && !WriteFinished;
            IsConnected = false;
            ReleaseWrite.TrySetResult();
            return default;
        }
    }
}
