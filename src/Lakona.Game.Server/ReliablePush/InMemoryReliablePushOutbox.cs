namespace Lakona.Game.Server.ReliablePush;

internal sealed class InMemoryReliablePushOutbox : IReliablePushOutbox
{
    private readonly Lock gate = new();
    private readonly ReliablePushOptions options;
    private readonly Dictionary<string, OwnerState> owners = new(StringComparer.Ordinal);

    public InMemoryReliablePushOutbox(ReliablePushOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async ValueTask<long> PublishAsync(
        string ownerKey,
        string kind,
        object payload,
        Func<ReliablePushRecord, ValueTask> deliver,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(deliver);

        var owner = GetOrCreateOwner(ownerKey);
        await owner.Serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (owner.ContinuityLost)
            {
                throw new ReliablePushContinuityLostException();
            }

            if (owner.Pending.Count >= Math.Max(1, options.MaxPendingPerSession))
            {
                owner.ContinuityLost = true;
                throw new ReliablePushContinuityLostException();
            }

            var record = new ReliablePushRecord
            {
                OwnerKey = ownerKey,
                Kind = kind,
                Payload = payload,
                Sequence = ++owner.LastSequence,
                CreatedAtUtc = DateTime.UtcNow,
            };
            owner.Pending.Add(record);
            await DeliverAsync(record, deliver, cancellationToken).ConfigureAwait(false);
            return record.Sequence;
        }
        finally
        {
            owner.Serial.Release();
        }
    }

    public async ValueTask ReplayPendingAsync(
        string ownerKey,
        Func<ReliablePushRecord, ValueTask> deliver,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        ArgumentNullException.ThrowIfNull(deliver);
        var owner = TryGetOwner(ownerKey);
        if (owner is null)
        {
            return;
        }

        await owner.Serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (owner.ContinuityLost)
            {
                throw new ReliablePushContinuityLostException();
            }

            foreach (var record in owner.Pending.OrderBy(static record => record.Sequence).ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DeliverAsync(record, deliver, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            owner.Serial.Release();
        }
    }

    public async ValueTask AckAsync(
        string ownerKey,
        long sequence,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        if (sequence <= 0)
        {
            return;
        }

        var owner = TryGetOwner(ownerKey);
        if (owner is null)
        {
            return;
        }

        await owner.Serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            owner.Pending.RemoveAll(record => record.Sequence <= sequence);
        }
        finally
        {
            owner.Serial.Release();
        }
    }

    public async ValueTask RemoveAsync(
        string ownerKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        OwnerState? owner;
        lock (gate)
        {
            owners.TryGetValue(ownerKey, out owner);
        }

        if (owner is null)
        {
            return;
        }

        await owner.Serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (gate)
            {
                owners.Remove(ownerKey);
            }
        }
        finally
        {
            owner.Serial.Release();
        }
    }

    public long GetLastSequence(string ownerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        var owner = TryGetOwner(ownerKey);
        return owner?.LastSequence ?? 0;
    }

    private OwnerState GetOrCreateOwner(string ownerKey)
    {
        lock (gate)
        {
            if (!owners.TryGetValue(ownerKey, out var owner))
            {
                owner = new OwnerState();
                owners.Add(ownerKey, owner);
            }

            return owner;
        }
    }

    private OwnerState? TryGetOwner(string ownerKey)
    {
        lock (gate)
        {
            return owners.TryGetValue(ownerKey, out var owner) ? owner : null;
        }
    }

    private static async ValueTask DeliverAsync(
        ReliablePushRecord record,
        Func<ReliablePushRecord, ValueTask> deliver,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        record.AttemptCount += 1;
        record.LastAttemptAtUtc = DateTime.UtcNow;
        await deliver(record).ConfigureAwait(false);
    }

    private sealed class OwnerState
    {
        public SemaphoreSlim Serial { get; } = new(1, 1);

        public long LastSequence { get; set; }

        public bool ContinuityLost { get; set; }

        public List<ReliablePushRecord> Pending { get; } = [];
    }
}
