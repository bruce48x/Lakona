namespace Lakona.Game.Server.Actors;

public sealed record ActorRuntimeDiagnosticsSnapshot(
    IReadOnlyList<ActorTypeDiagnosticsSnapshot> ActorTypes);

public sealed record ActorTypeDiagnosticsSnapshot(
    string ActorType,
    int ActiveCount,
    int MailboxQueuedSum,
    int MailboxQueuedMax,
    long MailboxEnqueuedCount,
    long MailboxProcessedCount,
    long MailboxRejectedCount);
