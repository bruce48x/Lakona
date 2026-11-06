namespace Lakona.Rpc.Core;

internal sealed class SerializedFrameSender : IDisposable
{
    private readonly RpcKeepAliveState _keepAliveState;
    private readonly ITransport _transport;
    private readonly object _gate = new();
    private readonly LinkedList<SendWork> _queue = new();
    private readonly Action<object?> _cancelQueued;
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
        lock (_gate)
        {
            // Dequeuing or disposal already claimed completion if the node was detached.
            if (node.List is null) return;
            _queue.Remove(node);
        }
        node.Value.Completion.TrySetCanceled(node.Value.Cancellation);
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
            try
            {
                work.Cancellation.ThrowIfCancellationRequested();
                await _transport.SendFrameAsync(work.Frame, work.Cancellation).ConfigureAwait(false);
                _keepAliveState.MarkSent();
                work.Completion.TrySetResult(true);
            }
            catch (OperationCanceledException) { work.Completion.TrySetCanceled(work.Cancellation); }
            catch (Exception error) { work.Completion.TrySetException(error); }

            lock (_gate)
            {
                var first = _queue.First;
                if (first is null)
                {
                    _draining = false;
                    return;
                }
                work = first.Value;
                _queue.RemoveFirst();
            }
        }
    }

    public void Dispose()
    {
        SendWork[] pending;
        lock (_gate)
        {
            _disposed = true;
            pending = _queue.ToArray();
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
