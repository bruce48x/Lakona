namespace Lakona.Game.Server.Actors;

/// <summary>
/// Captures aggregate mailbox counters for one local actor.
/// </summary>
/// <param name="Capacity">The configured mailbox capacity.</param>
/// <param name="QueuedCount">The number of messages currently queued.</param>
/// <param name="EnqueuedCount">The total number of messages accepted by the mailbox.</param>
/// <param name="ProcessedCount">The total number of messages processed by the actor.</param>
/// <param name="RejectedCount">The total number of messages rejected by the mailbox.</param>
/// <param name="IsCompleted">Whether the mailbox has completed and no longer accepts messages.</param>
public readonly record struct ActorMailboxMetrics(
    int Capacity,
    int QueuedCount,
    long EnqueuedCount,
    long ProcessedCount,
    long RejectedCount,
    bool IsCompleted);
