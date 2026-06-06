namespace Lakona.Actor;

public enum ActorCallTimeoutReason
{
    ResponseTimeout = 0,
    QueueTimeout = 1
}

public sealed record ActorCallTimeout(
    ActorId? Caller,
    ActorId Target,
    string RequestType,
    TimeSpan QueueTimeout,
    TimeSpan ResponseTimeout,
    TimeSpan Elapsed,
    ActorCallTimeoutReason Reason,
    IReadOnlyList<ActorId> CallChain);
