using Lakona.Game.Server.Sessions;

namespace Lakona.Game.Server.ReliablePush;

internal sealed class InMemoryReliablePushOutbox : IReliablePushOutbox
{
    private readonly Lock gate = new();
    private readonly ReliablePushOptions options;
    private readonly NotificationByteBudget<string> byteBudget;
    private readonly Dictionary<string, OwnerState> owners = new(StringComparer.Ordinal);

    public InMemoryReliablePushOutbox(ReliablePushOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        byteBudget = new(options.MaxPendingBytesPerSession, options.MaxPendingBytesPerProcess);
    }

    public async ValueTask<long> PublishAsync(
        string ownerKey,
        string kind,
        object payload,
        Func<ReliablePushRecord, ValueTask> deliver,
        CancellationToken cancellationToken = default,
        long retainedBytes = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(deliver);
        if (retainedBytes < 0) throw new ArgumentOutOfRangeException(nameof(retainedBytes));
        cancellationToken.ThrowIfCancellationRequested();

        var owner = GetOrCreateOwner(ownerKey);
        IDisposable reservation;
        lock (owner.Gate)
        {
            if (owner.Removed) throw new InvalidOperationException("Reliable push owner was removed.");
            if (owner.ContinuityLost) throw new ReliablePushContinuityLostException();
            var reserved = owner.Pending.Count + owner.WaitingPublications < Math.Max(1, options.MaxPendingPerSession)
                ? byteBudget.TryReserve(ownerKey, checked(128 + retainedBytes)) : null;
            if (reserved is null)
            {
                owner.ContinuityLost = true;
                throw new ReliablePushContinuityLostException(
                    newlyLost: true, owner.Pending.Count, owner.LastSequence, owner.AcknowledgedSequence);
            }
            reservation = reserved;
            owner.WaitingPublications++;
        }

        var acquired = false;
        var retained = false;
        try
        {
            await owner.Serial.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            ReliablePushRecord record;
            lock (owner.Gate)
            {
                if (owner.Removed) throw new InvalidOperationException("Reliable push owner was removed.");
                if (owner.ContinuityLost) throw new ReliablePushContinuityLostException();
                record = new ReliablePushRecord
                {
                    OwnerKey = ownerKey,
                    Kind = kind,
                    Payload = payload,
                    Sequence = ++owner.LastSequence,
                    CreatedAtUtc = DateTime.UtcNow,
                    ByteReservation = reservation,
                    ActiveDeliveries = 1,
                };
                owner.Pending.Add(record);
                owner.WaitingPublications--;
                retained = true;
            }
            try { await DeliverAsync(record, deliver, cancellationToken).ConfigureAwait(false); }
            finally { ReleaseDelivery(owner, record); }
            return record.Sequence;
        }
        finally
        {
            if (!retained)
            {
                lock (owner.Gate) owner.WaitingPublications--;
                reservation.Dispose();
            }
            if (acquired) owner.Serial.Release();
        }
    }

    public async ValueTask ReplayPendingAsync(
        string ownerKey,
        Func<ReliablePushRecord, ValueTask> deliver,
        Func<ValueTask>? onReplayed = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        ArgumentNullException.ThrowIfNull(deliver);
        // Even an empty recovery must finish under the same lock as the first publication.
        var owner = onReplayed is null ? TryGetOwner(ownerKey) : GetOrCreateOwner(ownerKey);
        if (owner is null)
        {
            return;
        }

        await owner.Serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ReliablePushRecord[] pending;
            lock (owner.Gate)
            {
                if (owner.Removed) throw new InvalidOperationException("Reliable push owner was removed.");
                if (owner.ContinuityLost)
                {
                    throw new ReliablePushContinuityLostException();
                }

                pending = owner.Pending.OrderBy(static record => record.Sequence).ToArray();
                foreach (var record in pending) record.ActiveDeliveries++;
            }

            try
            {
                foreach (var record in pending)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await DeliverAsync(record, deliver, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                foreach (var record in pending) ReleaseDelivery(owner, record);
            }
            if (onReplayed is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await onReplayed().ConfigureAwait(false);
            }
        }
        finally
        {
            owner.Serial.Release();
        }
    }

    public ValueTask AckAsync(
        string ownerKey,
        long sequence,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        if (sequence <= 0)
        {
            return default;
        }

        var owner = TryGetOwner(ownerKey);
        if (owner is null)
        {
            return default;
        }

        lock (owner.Gate)
        {
            owner.Pending.RemoveAll(record =>
            {
                if (record.Sequence > sequence) return false;
                RetireRecord(record);
                return true;
            });
            owner.AcknowledgedSequence = Math.Max(owner.AcknowledgedSequence, Math.Min(sequence, owner.LastSequence));
        }
        return default;
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
            lock (owner.Gate)
            {
                owner.Removed = true;
                foreach (var record in owner.Pending) RetireRecord(record);
                owner.Pending.Clear();
            }
            lock (gate)
            {
                if (owners.TryGetValue(ownerKey, out var current) && ReferenceEquals(current, owner))
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
        if (owner is null)
        {
            return 0;
        }

        lock (owner.Gate)
        {
            return owner.LastSequence;
        }
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

    private static void RetireRecord(ReliablePushRecord record)
    {
        record.Retired = true;
        if (record.ActiveDeliveries == 0) record.ByteReservation?.Dispose();
    }

    private static void ReleaseDelivery(OwnerState owner, ReliablePushRecord record)
    {
        lock (owner.Gate)
        {
            record.ActiveDeliveries--;
            if (record.Retired && record.ActiveDeliveries == 0) record.ByteReservation?.Dispose();
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
        public object Gate { get; } = new();

        public SemaphoreSlim Serial { get; } = new(1, 1);

        public long LastSequence { get; set; }

        public long AcknowledgedSequence { get; set; }

        public bool ContinuityLost { get; set; }
        public bool Removed { get; set; }
        public int WaitingPublications { get; set; }

        public List<ReliablePushRecord> Pending { get; } = [];
    }
}
