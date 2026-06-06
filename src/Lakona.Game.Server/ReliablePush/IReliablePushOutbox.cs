namespace Lakona.Game.Server.ReliablePush;

public interface IReliablePushOutbox
{
    ValueTask<long> PublishAsync(
        string ownerKey,
        string kind,
        object payload,
        Func<ReliablePushRecord, ValueTask> deliver,
        CancellationToken cancellationToken = default);

    ValueTask ReplayPendingAsync(
        string ownerKey,
        Func<ReliablePushRecord, ValueTask> deliver,
        CancellationToken cancellationToken = default);

    ValueTask AckAsync(string ownerKey, long sequence, CancellationToken cancellationToken = default);

    long GetLastSequence(string ownerKey);
}
