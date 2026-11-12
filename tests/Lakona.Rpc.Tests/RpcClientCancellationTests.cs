using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;

namespace Lakona.Rpc.Tests;

public sealed class RpcClientCancellationTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Cancellation_failure_preserves_send_drain_and_shared_cleanup(bool receiveFails, bool disposalFails)
    {
        var peer = new ThrowingCancellationTransport { FailDisposal = disposalFails };
        var client = new RpcClientRuntime(peer, new JsonRpcSerializer());
        var disconnected = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += error => disconnected.TrySetResult(error);
        Task<int>? request = null;
        try
        {
            await client.StartAsync(CancellationToken.None);
            await peer.Receiving.Task.WaitAsync(Guard, CancellationToken.None);
            request = client.CallAsync(new RpcMethod<int, int>(1, 1), 42).AsTask();
            await peer.Writing.Task.WaitAsync(Guard, CancellationToken.None);
            Task? firstDisposal = null;
            if (receiveFails) peer.EndReceive.TrySetResult();
            else firstDisposal = client.DisposeAsync().AsTask();

            await peer.WriteCanceled.Task.WaitAsync(Guard, CancellationToken.None);
            Assert.False(disconnected.Task.IsCompleted);
            Assert.False(firstDisposal?.IsCompleted ?? false);
            Assert.Equal(0, peer.DisposeCount);
            peer.ReleaseWrite.TrySetResult();

            if (receiveFails)
            {
                Assert.Same(peer.ReceiveError, await disconnected.Task.WaitAsync(Guard, CancellationToken.None));
                Assert.Same(peer.ReceiveError, await Assert.ThrowsAsync<IOException>(() => request.WaitAsync(Guard)));
            }
            firstDisposal ??= client.DisposeAsync().AsTask();
            await peer.Disposing.Task.WaitAsync(Guard, CancellationToken.None);
            var secondDisposal = client.DisposeAsync().AsTask();
            Assert.False(firstDisposal.IsCompleted);
            Assert.False(secondDisposal.IsCompleted);
            peer.ReleaseDisposal.TrySetResult();

            var error = await Record.ExceptionAsync(() => firstDisposal.WaitAsync(Guard));
            Assert.True(Contains(error, peer.SendCancellationError), error?.ToString());
            if (!receiveFails) Assert.True(Contains(error, peer.ReceiveCancellationError), error?.ToString());
            if (disposalFails) Assert.True(Contains(error, peer.DisposalError), error?.ToString());
            Assert.Same(error, await Record.ExceptionAsync(() => secondDisposal.WaitAsync(Guard)));
            Assert.Same(error, await Record.ExceptionAsync(() => client.DisposeAsync().AsTask().WaitAsync(Guard)));
            Assert.Equal(1, peer.DisposeCount);
            Assert.True(peer.WriteFinished);
            Assert.True(peer.ReceiveFinished);
            Assert.False(peer.DisposedDuringWrite);
            if (!receiveFails) await Assert.ThrowsAsync<ObjectDisposedException>(() => request.WaitAsync(Guard));
        }
        finally
        {
            peer.ReleaseWrite.TrySetResult();
            peer.ReleaseDisposal.TrySetResult();
            _ = await Record.ExceptionAsync(() => client.DisposeAsync().AsTask().WaitAsync(Guard));
            if (request is not null) _ = await Record.ExceptionAsync(() => request.WaitAsync(Guard));
        }
    }

    [Fact]
    public async Task Throwing_connect_cancellation_still_joins_startup_before_disposing_transport()
    {
        var peer = new ThrowingCancellationTransport { HoldConnect = true };
        var client = new RpcClientRuntime(peer, new JsonRpcSerializer());
        var starting = client.StartAsync(CancellationToken.None).AsTask();
        try
        {
            await peer.Connecting.Task.WaitAsync(Guard, CancellationToken.None);
            var first = client.DisposeAsync().AsTask();
            var second = client.DisposeAsync().AsTask();
            await peer.ConnectCanceled.Task.WaitAsync(Guard, CancellationToken.None);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.Equal(0, peer.DisposeCount);
            peer.ReleaseConnect.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting.WaitAsync(Guard));
            await peer.Disposing.Task.WaitAsync(Guard, CancellationToken.None);
            peer.ReleaseDisposal.TrySetResult();
            var error = await Record.ExceptionAsync(() => first.WaitAsync(Guard));
            Assert.True(Contains(error, peer.ConnectCancellationError), error?.ToString());
            Assert.Same(error, await Record.ExceptionAsync(() => second.WaitAsync(Guard)));
            Assert.Equal(1, peer.DisposeCount);
            Assert.False(peer.Receiving.Task.IsCompleted);
        }
        finally
        {
            peer.ReleaseConnect.TrySetResult();
            peer.ReleaseDisposal.TrySetResult();
            _ = await Record.ExceptionAsync(() => starting.WaitAsync(Guard));
            _ = await Record.ExceptionAsync(() => client.DisposeAsync().AsTask().WaitAsync(Guard));
        }
    }

    private static bool Contains(Exception? error, Exception expected) =>
        ReferenceEquals(error, expected) || error is AggregateException aggregate &&
        aggregate.Flatten().InnerExceptions.Contains(expected);

    private sealed class ThrowingCancellationTransport : ITransport
    {
        public TaskCompletionSource Connecting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ConnectCanceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseConnect { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Receiving { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource EndReceive { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Writing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource WriteCanceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDisposal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception ConnectCancellationError { get; } = new InvalidOperationException("Connect cancellation callback failed.");
        public Exception ReceiveCancellationError { get; } = new InvalidOperationException("Receive cancellation callback failed.");
        public Exception SendCancellationError { get; } = new InvalidOperationException("Send cancellation callback failed.");
        public IOException ReceiveError { get; } = new("Receive failed.");
        public Exception DisposalError { get; } = new InvalidOperationException("Transport disposal failed.");
        public bool HoldConnect;
        public bool FailDisposal;
        public bool WriteFinished;
        public bool ReceiveFinished;
        public bool DisposedDuringWrite;
        public int DisposeCount;
        public bool IsConnected { get; private set; }

        public async ValueTask ConnectAsync(CancellationToken ct = default)
        {
            if (HoldConnect)
            {
                using var registration = ct.Register(() =>
                {
                    ConnectCanceled.TrySetResult();
                    throw ConnectCancellationError;
                });
                Connecting.TrySetResult();
                await ReleaseConnect.Task;
                ct.ThrowIfCancellationRequested();
            }
            IsConnected = true;
        }

        public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            using var registration = ct.Register(() =>
            {
                WriteCanceled.TrySetResult();
                throw SendCancellationError;
            });
            Writing.TrySetResult();
            try
            {
                await ReleaseWrite.Task;
                Assert.Equal((byte)RpcFrameType.Request, frame.Span[0]);
                ct.ThrowIfCancellationRequested();
            }
            finally { WriteFinished = true; }
        }

        public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            using var registration = ct.Register(() =>
            {
                EndReceive.TrySetCanceled(ct);
                throw ReceiveCancellationError;
            });
            Receiving.TrySetResult();
            try
            {
                await EndReceive.Task;
                throw ReceiveError;
            }
            finally { ReceiveFinished = true; }
        }

        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref DisposeCount);
            DisposedDuringWrite = Writing.Task.IsCompleted && !WriteFinished;
            IsConnected = false;
            Disposing.TrySetResult();
            await ReleaseDisposal.Task;
            if (FailDisposal) throw DisposalError;
        }
    }
}
