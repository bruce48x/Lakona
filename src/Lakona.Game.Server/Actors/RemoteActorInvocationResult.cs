namespace Lakona.Game.Server.Actors;

public sealed class RemoteActorInvocationResult
{
    public RemoteActorInvocationResult(
        RemoteActorStatus status,
        ReadOnlyMemory<byte> payload,
        string? message = null)
    {
        Status = status;
        Payload = payload.ToArray();
        Message = message;
    }

    public RemoteActorStatus Status { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    public string? Message { get; }

    public static RemoteActorInvocationResult Accepted()
    {
        return new RemoteActorInvocationResult(RemoteActorStatus.Accepted, ReadOnlyMemory<byte>.Empty);
    }

    public static RemoteActorInvocationResult Replied(ReadOnlyMemory<byte> payload)
    {
        return new RemoteActorInvocationResult(RemoteActorStatus.Replied, payload);
    }

    public static RemoteActorInvocationResult Failed(RemoteActorStatus status, string message)
    {
        return new RemoteActorInvocationResult(status, ReadOnlyMemory<byte>.Empty, message);
    }
}
