namespace Lakona.Game.Server.ReliablePush;

internal interface IReliablePushOutbox
{
    ValueTask<long> PublishAsync(
        string ownerKey,
        string kind,
        object payload,
        Func<ReliablePushRecord, ValueTask> deliver,
        CancellationToken cancellationToken = default,
        long retainedBytes = 0);

    ValueTask ReplayPendingAsync(
        string ownerKey,
        Func<ReliablePushRecord, ValueTask> deliver,
        Func<ValueTask>? onReplayed = null,
        CancellationToken cancellationToken = default);

    ValueTask AckAsync(string ownerKey, long sequence, CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(string ownerKey, CancellationToken cancellationToken = default);

    long GetLastSequence(string ownerKey);
}
