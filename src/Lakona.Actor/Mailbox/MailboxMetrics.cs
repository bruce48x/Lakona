namespace Lakona.Actor;

public readonly record struct MailboxMetrics(
    int Capacity,
    int QueuedCount,
    long EnqueuedCount,
    long ProcessedCount,
    long RejectedCount,
    bool IsCompleted);
