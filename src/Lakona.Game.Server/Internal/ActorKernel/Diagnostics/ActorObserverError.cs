namespace Lakona.Game.Server.Internal.ActorKernel;

internal enum ActorObserverErrorSource
{
    DeadLetterHandler = 0,
    SlowMessageHandler = 1,
    CallTimeoutHandler = 2,
    MessageInterceptorBefore = 3,
    MessageInterceptorAfter = 4
}

internal sealed record ActorObserverError(
    ActorObserverErrorSource Source,
    ActorId? ActorId,
    string MessageType,
    Exception Exception);
