namespace Lakona.Rpc.Core;

internal sealed class SerializedFrameSender : IDisposable
{
    private readonly RpcKeepAliveState _keepAliveState;
    private readonly ITransport _transport;
    private readonly object _gate = new();
    private readonly LinkedList<SendWork> _queue = new();
    private readonly Action<object?> _cancelQueued;
    private const long MaximumPendingBytes = 128L * 1024 * 1024;
    private const int MaximumPendingFrames = 65_536;
    private long _pendingBytes;
    private int _pendingFrames;
    private bool _draining;
    private bool _disposed;

    public SerializedFrameSender(ITransport transport, RpcKeepAliveState keepAliveState)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _keepAliveState = keepAliveState ?? throw new ArgumentNullException(nameof(keepAliveState));
        _cancelQueued = CancelQueued;
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var work = new SendWork(frame, ct);
        LinkedListNode<SendWork>? queued = null;
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SerializedFrameSender));
            if (frame.Length > MaximumPendingBytes - _pendingBytes || _pendingFrames >= MaximumPendingFrames)
                throw new InvalidOperationException("RPC send queue capacity exceeded (128 MiB or 65536 frames per connection).");
            _pendingBytes += frame.Length;
            _pendingFrames++;
            if (_draining)
                queued = _queue.AddLast(work);
            else
                _draining = true;
        }

        // An idle writer claimed this frame under the lock. Only queued frames need unlinking.
        if (queued is null)
            _ = DrainAsync(work);
        else if (ct.CanBeCanceled)
            return WaitForSendAsync(work.Completion.Task, ct.Register(_cancelQueued, queued));

        return new ValueTask(work.Completion.Task);
    }

    private void CancelQueued(object? state)
    {
        var node = (LinkedListNode<SendWork>)state!;
        SendWork work;
        lock (_gate)
        {
            // Dequeuing or disposal already claimed completion if the node was detached.
            if (node.List is null) return;
            work = node.Value;
            _queue.Remove(node);
            _pendingBytes -= work.Frame.Length;
            _pendingFrames--;
            // The cancellation registration can outlive task completion while its
            // async waiter unwinds. It must not keep the canceled payload alive.
            node.Value = default;
        }
        work.Completion.TrySetCanceled(work.Cancellation);
    }

    private static async ValueTask WaitForSendAsync(Task completion, CancellationTokenRegistration registration)
    {
        try { await completion.ConfigureAwait(false); }
        finally { registration.Dispose(); }
    }

    private async Task DrainAsync(SendWork work)
    {
        while (true)
        {
            Exception? failure = null;
            try
            {
                work.Cancellation.ThrowIfCancellationRequested();
                await _transport.SendFrameAsync(work.Frame, work.Cancellation).ConfigureAwait(false);
                _keepAliveState.MarkSent();
            }
            catch (Exception error) { failure = error; }

            var completion = work.Completion;
            var cancellation = work.Cancellation;
            bool more;
            lock (_gate)
            {
                _pendingBytes -= work.Frame.Length;
                _pendingFrames--;
                var first = _queue.First;
                more = first is not null;
                if (first is null) _draining = false;
                else
                {
                    work = first.Value;
                    _queue.RemoveFirst();
                }
            }
            // An awaiting producer may immediately submit another frame.
            if (failure is OperationCanceledException) completion.TrySetCanceled(cancellation);
            else if (failure is not null) completion.TrySetException(failure);
            else completion.TrySetResult(true);
            if (!more) return;
        }
    }

    public void Dispose()
    {
        SendWork[] pending;
        lock (_gate)
        {
            _disposed = true;
            pending = _queue.ToArray();
            foreach (var work in pending) _pendingBytes -= work.Frame.Length;
            _pendingFrames -= pending.Length;
            _queue.Clear();
        }
        foreach (var work in pending)
            work.Completion.TrySetException(new ObjectDisposedException(nameof(SerializedFrameSender)));
    }

    private readonly struct SendWork
    {
        public SendWork(ReadOnlyMemory<byte> frame, CancellationToken cancellation)
        {
            Frame = frame;
            Cancellation = cancellation;
            Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        public ReadOnlyMemory<byte> Frame { get; }
        public CancellationToken Cancellation { get; }
        public TaskCompletionSource<bool> Completion { get; }
    }
}
