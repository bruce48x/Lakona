namespace Lakona.Game.Server.ReliablePush;

internal sealed class InMemoryReliablePushOutbox : IReliablePushOutbox
{
    private readonly Lock _gate = new();
    private readonly ReliablePushOptions _options;
    private readonly Dictionary<string, OwnerState> _owners = new(StringComparer.Ordinal);

    public InMemoryReliablePushOutbox(ReliablePushOptions options)
    {
        _options = options;
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

        ReliablePushRecord record;
        lock (_gate)
        {
            var owner = GetOrCreateOwner(ownerKey);
            PruneExpired(owner);
            record = new ReliablePushRecord
            {
                OwnerKey = ownerKey,
                Kind = kind,
                Payload = payload,
                Sequence = ++owner.LastSequence,
                CreatedAtUtc = DateTime.UtcNow
            };
            owner.Pending.Add(record);
            TrimOverflow(owner);
        }

        await DeliverAsync(record, deliver, cancellationToken).ConfigureAwait(false);
        return record.Sequence;
    }

    public async ValueTask ReplayPendingAsync(
        string ownerKey,
        Func<ReliablePushRecord, ValueTask> deliver,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        ArgumentNullException.ThrowIfNull(deliver);

        ReliablePushRecord[] records;
        lock (_gate)
        {
            if (!_owners.TryGetValue(ownerKey, out var owner))
            {
                return;
            }

            PruneExpired(owner);
            records = owner.Pending.OrderBy(static record => record.Sequence).ToArray();
        }

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DeliverAsync(record, deliver, cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask AckAsync(string ownerKey, long sequence, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        if (sequence <= 0)
        {
            return ValueTask.CompletedTask;
        }

        lock (_gate)
        {
            if (!_owners.TryGetValue(ownerKey, out var owner))
            {
                return ValueTask.CompletedTask;
            }

            owner.Pending.RemoveAll(record => record.Sequence <= sequence);
        }

        return ValueTask.CompletedTask;
    }

    public long GetLastSequence(string ownerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        lock (_gate)
        {
            return _owners.TryGetValue(ownerKey, out var owner) ? owner.LastSequence : 0;
        }
    }

    private OwnerState GetOrCreateOwner(string ownerKey)
    {
        if (_owners.TryGetValue(ownerKey, out var owner))
        {
            return owner;
        }

        owner = new OwnerState();
        _owners.Add(ownerKey, owner);
        return owner;
    }

    private void PruneExpired(OwnerState owner)
    {
        var cutoff = DateTime.UtcNow - _options.Retention;
        owner.Pending.RemoveAll(record => record.CreatedAtUtc < cutoff);
    }

    private void TrimOverflow(OwnerState owner)
    {
        var maxPending = Math.Max(1, _options.MaxPendingPerOwner);
        if (owner.Pending.Count <= maxPending)
        {
            return;
        }

        owner.Pending.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));
        owner.Pending.RemoveRange(0, owner.Pending.Count - maxPending);
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
        public long LastSequence { get; set; }

        public List<ReliablePushRecord> Pending { get; } = new();
    }
}
