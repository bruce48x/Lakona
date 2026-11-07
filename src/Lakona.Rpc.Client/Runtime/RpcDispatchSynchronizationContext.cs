using System.Collections.Concurrent;

namespace Lakona.Rpc.Client;

// A serial execution environment for clients without an engine-owned context.
internal sealed class RpcDispatchSynchronizationContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private int _scheduled;

    public override void Post(SendOrPostCallback callback, object? state)
    {
        _queue.Enqueue((callback, state));
        if (Interlocked.CompareExchange(ref _scheduled, 1, 0) == 0)
            ThreadPool.QueueUserWorkItem(_ => Drain());
    }

    private void Drain()
    {
        var previous = Current;
        SetSynchronizationContext(this);
        try
        {
            do
            {
                while (_queue.TryDequeue(out var work)) work.Callback(work.State);
                // Publish idle before checking the queue. A release-only write permits
                // the empty check to race ahead: Post can then see the old busy flag
                // while this drain sees an empty queue, leaving work with no owner.
                Interlocked.Exchange(ref _scheduled, 0);
            }
            while (!_queue.IsEmpty && Interlocked.CompareExchange(ref _scheduled, 1, 0) == 0);
        }
        finally { SetSynchronizationContext(previous); }
    }
}
