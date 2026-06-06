using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.ReliablePush;

public delegate ValueTask ReliablePushDeliver<TCallback, in TPayload>(
    TCallback callback,
    ReliablePushSequence sequence,
    TPayload payload,
    CancellationToken cancellationToken)
    where TCallback : class;
