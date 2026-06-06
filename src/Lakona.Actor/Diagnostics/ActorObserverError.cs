namespace Lakona.Actor;

public enum ActorObserverErrorSource
{
    DeadLetterHandler = 0,
    SlowMessageHandler = 1,
    CallTimeoutHandler = 2,
    MessageInterceptorBefore = 3,
    MessageInterceptorAfter = 4
}

public sealed record ActorObserverError(
    ActorObserverErrorSource Source,
    ActorId? ActorId,
    string MessageType,
    Exception Exception);
