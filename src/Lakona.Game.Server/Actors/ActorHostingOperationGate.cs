using System.Collections.Concurrent;

namespace Lakona.Game.Server.Actors;

internal sealed class ActorHostingOperationGate
{
    private readonly ConcurrentDictionary<ActorId, Entry> _entries = new();

    public async ValueTask<IAsyncDisposable> EnterAsync(
        ActorId actorId,
        CancellationToken cancellationToken)
    {
        Entry entry;
        do
        {
            entry = _entries.GetOrAdd(actorId, static _ => new Entry());
        }
        while (!entry.TryAddRef());

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
        if (entry.ReleaseRefAndRetireIfUnused())
        {
            _entries.TryRemove(new KeyValuePair<ActorId, Entry>(actorId, entry));
        }
    }

    private sealed class Entry
    {
        private const int Retired = -1;
        private int _refCount;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public bool TryAddRef()
        {
            while (true)
            {
                var current = Volatile.Read(ref _refCount);
                if (current == Retired)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _refCount, current + 1, current) == current)
                {
                    return true;
                }
            }
        }

        public bool ReleaseRefAndRetireIfUnused()
        {
            return Interlocked.Decrement(ref _refCount) == 0 &&
                Interlocked.CompareExchange(ref _refCount, Retired, 0) == 0;
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
