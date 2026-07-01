using System.Collections.Concurrent;

namespace Lakona.Game.Server.Actors;

internal sealed class ActorHostingOperationGate
{
    private readonly ConcurrentDictionary<ActorId, Entry> _entries = new();

    public async ValueTask<IAsyncDisposable> EnterAsync(
        ActorId actorId,
        CancellationToken cancellationToken)
    {
        var entry = _entries.GetOrAdd(actorId, static _ => new Entry());
        entry.AddRef();
        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new Releaser(this, actorId, entry);
        }
        catch
        {
            ReleaseRef(actorId, entry);
            throw;
        }
    }

    private void Release(ActorId actorId, Entry entry)
    {
        entry.Semaphore.Release();
        ReleaseRef(actorId, entry);
    }

    private void ReleaseRef(ActorId actorId, Entry entry)
    {
        if (entry.ReleaseRef() == 0)
        {
            _entries.TryRemove(new KeyValuePair<ActorId, Entry>(actorId, entry));
        }
    }

    private sealed class Entry
    {
        private int _refCount;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public void AddRef()
        {
            Interlocked.Increment(ref _refCount);
        }

        public int ReleaseRef()
        {
            return Interlocked.Decrement(ref _refCount);
        }
    }

    private sealed class Releaser(ActorHostingOperationGate owner, ActorId actorId, Entry entry) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Release(actorId, entry);
            }

            return default;
        }
    }
}
