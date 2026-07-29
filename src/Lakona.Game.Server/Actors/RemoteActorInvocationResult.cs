namespace Lakona.Game.Server.Actors;

public sealed class RemoteActorInvocationResult
{
    private RemoteActorInvocationResult(
        RemoteActorStatus status,
        object? reply,
        string? message,
        RemoteActorRetrySafety retrySafety)
    {
        Status = status;
        Reply = reply;
        Message = message;
        RetrySafety = retrySafety;
    }

    public RemoteActorStatus Status { get; }

    internal object? Reply { get; }

    public string? Message { get; }

    public RemoteActorRetrySafety RetrySafety { get; }

    public static RemoteActorInvocationResult Accepted()
    {
        return new RemoteActorInvocationResult(
            RemoteActorStatus.Accepted,
            reply: null,
            message: null,
            RemoteActorRetrySafety.Indeterminate);
    }

    public static RemoteActorInvocationResult Replied<T>(T reply)
    {
        return new RemoteActorInvocationResult(
            RemoteActorStatus.Replied,
            reply,
            message: null,
            RemoteActorRetrySafety.Indeterminate);
    }

    internal static RemoteActorInvocationResult Replied(object? reply)
    {
        return new RemoteActorInvocationResult(
            RemoteActorStatus.Replied,
            reply,
            message: null,
            RemoteActorRetrySafety.Indeterminate);
    }

    public static RemoteActorInvocationResult Failed(
        RemoteActorStatus status,
        string message,
        RemoteActorRetrySafety retrySafety = RemoteActorRetrySafety.Indeterminate)
    {
        return new RemoteActorInvocationResult(status, reply: null, message, retrySafety);
    }
}
