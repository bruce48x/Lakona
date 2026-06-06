using Lakona.Game.Abstractions;
using Lakona.Game.Server.Sessions;

namespace Lakona.Game.Server.ReliablePush;

public static class ReliablePushOutboxSessionExtensions
{
    public static ValueTask<long> PublishAsync(
        this IReliablePushOutbox outbox,
        GameSessionKey session,
        string kind,
        object payload,
        Func<ReliablePushRecord, ValueTask> deliver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        return outbox.PublishAsync(
            ReliablePushSessionOwnerKey.Create(session),
            kind,
            payload,
            deliver,
            cancellationToken);
    }

    public static ValueTask ReplayPendingAsync(
        this IReliablePushOutbox outbox,
        GameSessionKey session,
        Func<ReliablePushRecord, ValueTask> deliver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        return outbox.ReplayPendingAsync(
            ReliablePushSessionOwnerKey.Create(session),
            deliver,
            cancellationToken);
    }

    public static ValueTask AckAsync(
        this IReliablePushOutbox outbox,
        GameSessionKey session,
        long sequence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        return outbox.AckAsync(
            ReliablePushSessionOwnerKey.Create(session),
            sequence,
            cancellationToken);
    }

    public static long GetLastSequence(
        this IReliablePushOutbox outbox,
        GameSessionKey session)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        return outbox.GetLastSequence(ReliablePushSessionOwnerKey.Create(session));
    }
}
