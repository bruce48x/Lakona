namespace Lakona.Game.Server.Actors;

/// <summary>
/// Correlates best-effort remote cancellation with an Actor request on this node.
/// </summary>
internal sealed class ClusterActorCancellationRegistry : IDisposable
{
    private static readonly TimeSpan UnmatchedCancellationRetention = TimeSpan.FromMinutes(1);
    private readonly object gate = new();
    private readonly Dictionary<Guid, Entry> entries = [];
    private readonly TimeProvider timeProvider;
    private bool disposed;

    public ClusterActorCancellationRegistry(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public Registration Register(Guid invocationId, CancellationToken cancellationToken)
    {
        if (invocationId == Guid.Empty)
        {
            throw new ArgumentException("Invocation id is required.", nameof(invocationId));
        }

        Entry entry;
        CancellationTokenSource linkedSource;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!entries.TryGetValue(invocationId, out entry!))
            {
                entry = new Entry();
                entries.Add(invocationId, entry);
            }

            entry.ExpiryTimer?.Dispose();
            entry.ExpiryTimer = null;
            entry.Registrations++;
            linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                entry.CancellationSource.Token);
        }

        return new Registration(this, invocationId, entry, linkedSource);
    }

    public void Cancel(Guid invocationId)
    {
        if (invocationId == Guid.Empty)
        {
            return;
        }

        Entry entry;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            if (!entries.TryGetValue(invocationId, out entry!))
            {
                entry = new Entry();
                entries.Add(invocationId, entry);
            }

            if (entry.Registrations == 0 && entry.ExpiryTimer is null)
            {
                entry.ExpiryTimer = timeProvider.CreateTimer(
                    static state =>
                    {
                        var expiry = (ExpiryState)state!;
                        expiry.Registry.Expire(expiry.InvocationId, expiry.Entry);
                    },
                    new ExpiryState(this, invocationId, entry),
                    UnmatchedCancellationRetention,
                    Timeout.InfiniteTimeSpan);
            }
        }

        try
        {
            entry.CancellationSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        Entry[] removed;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            removed = [.. entries.Values];
            entries.Clear();
        }

        foreach (var entry in removed)
        {
            entry.Dispose();
        }
    }

    private void Release(Guid invocationId, Entry entry)
    {
        var disposeEntry = false;
        lock (gate)
        {
            if (entry.Registrations <= 0)
            {
                throw new InvalidOperationException(
                    "Cluster Actor cancellation registration was released twice.");
            }

            entry.Registrations--;
            if (entry.Registrations == 0
                && entries.TryGetValue(invocationId, out var current)
                && ReferenceEquals(current, entry))
            {
                entries.Remove(invocationId);
                disposeEntry = true;
            }
        }

        if (disposeEntry)
        {
            entry.Dispose();
        }
    }

    private void Expire(Guid invocationId, Entry entry)
    {
        var disposeEntry = false;
        lock (gate)
        {
            if (entry.Registrations == 0
                && entries.TryGetValue(invocationId, out var current)
                && ReferenceEquals(current, entry))
            {
                entries.Remove(invocationId);
                disposeEntry = true;
            }
        }

        if (disposeEntry)
        {
            entry.Dispose();
        }
    }

    internal sealed class Registration : IDisposable
    {
        private ClusterActorCancellationRegistry? registry;
        private readonly Guid invocationId;
        private readonly Entry entry;
        private readonly CancellationTokenSource linkedSource;

        internal Registration(
            ClusterActorCancellationRegistry registry,
            Guid invocationId,
            Entry entry,
            CancellationTokenSource linkedSource)
        {
            this.registry = registry;
            this.invocationId = invocationId;
            this.entry = entry;
            this.linkedSource = linkedSource;
        }

        public CancellationToken Token => linkedSource.Token;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref registry, null);
            if (owner is null)
            {
                return;
            }

            linkedSource.Dispose();
            owner.Release(invocationId, entry);
        }
    }

    internal sealed class Entry : IDisposable
    {
        public CancellationTokenSource CancellationSource { get; } = new();

        public int Registrations { get; set; }

        public ITimer? ExpiryTimer { get; set; }

        public void Dispose()
        {
            ExpiryTimer?.Dispose();
            CancellationSource.Dispose();
        }
    }

    private sealed record ExpiryState(
        ClusterActorCancellationRegistry Registry,
        Guid InvocationId,
        Entry Entry);
}
