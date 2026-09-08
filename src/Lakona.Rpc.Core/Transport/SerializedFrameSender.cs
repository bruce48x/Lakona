namespace Lakona.Rpc.Core;

internal sealed class SerializedFrameSender : IDisposable
{
    private readonly RpcKeepAliveState _keepAliveState;
    private readonly ITransport _transport;
    private readonly object _gate = new();
    private readonly Queue<SendWork> _queue = new();
    private bool _draining;
    private bool _disposed;

    public SerializedFrameSender(ITransport transport, RpcKeepAliveState keepAliveState)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _keepAliveState = keepAliveState ?? throw new ArgumentNullException(nameof(keepAliveState));
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var work = new SendWork(frame, ct);
        bool start;
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SerializedFrameSender));
            _queue.Enqueue(work);
            start = !_draining;
            _draining = true;
        }
        var registration = ct.Register(() =>
        {
            lock (_gate)
            {
                if (work.Started) return;
                work.Canceled = true;
            }
            work.Completion.TrySetCanceled(ct);
        });
        if (start) _ = DrainAsync();
        // The caller retains frame ownership until this task completes, including while queued.
        return WaitForSendAsync(work, registration);
    }

    private static async ValueTask WaitForSendAsync(SendWork work, CancellationTokenRegistration registration)
    {
        try { await work.Completion.Task.ConfigureAwait(false); }
        finally { registration.Dispose(); }
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            SendWork work;
            lock (_gate)
            {
                if (_queue.Count == 0) { _draining = false; return; }
                work = _queue.Dequeue();
                if (work.Canceled) continue;
                work.Started = true;
            }
            try
            {
                work.Cancellation.ThrowIfCancellationRequested();
                await _transport.SendFrameAsync(work.Frame, work.Cancellation).ConfigureAwait(false);
                _keepAliveState.MarkSent();
                work.Completion.TrySetResult(true);
            }
            catch (OperationCanceledException) { work.Completion.TrySetCanceled(work.Cancellation); }
            catch (Exception error) { work.Completion.TrySetException(error); }
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

    private sealed class SendWork
    {
        public SendWork(ReadOnlyMemory<byte> frame, CancellationToken cancellation)
        { Frame = frame; Cancellation = cancellation; }
        public ReadOnlyMemory<byte> Frame { get; }
        public CancellationToken Cancellation { get; }
        public bool Started { get; set; }
        public bool Canceled { get; set; }
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
