namespace Lakona.Game.Server.Actors;

public readonly record struct ActorMailboxMetrics(
    int Capacity,
    int QueuedCount,
    long EnqueuedCount,
    long ProcessedCount,
    long RejectedCount,
    bool IsCompleted);
