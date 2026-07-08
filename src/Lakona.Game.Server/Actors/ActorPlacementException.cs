namespace Lakona.Game.Server.Actors;

public sealed class ActorPlacementException : InvalidOperationException
{
    public ActorPlacementException(
        Type actorType,
        ActorId actorId,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ActorType = actorType ?? throw new ArgumentNullException(nameof(actorType));
        ActorId = actorId;
    }

    public Type ActorType { get; }

    public ActorId ActorId { get; }
}
