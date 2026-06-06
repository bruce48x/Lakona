namespace Lakona.Game.Server.Actors;

public sealed record ActorDeadLetterDiagnostic(
    ActorId Target,
    object Message,
    string Reason);

public sealed record ActorSlowMessageDiagnostic(
    ActorId ActorId,
    object Message,
    TimeSpan Elapsed);

public sealed record ActorCallTimeoutDiagnostic(
    ActorId? Caller,
    ActorId Target,
    object Request,
    TimeSpan Timeout,
    ActorCallTimeoutReason Reason,
    IReadOnlyList<ActorId> CallChain);
