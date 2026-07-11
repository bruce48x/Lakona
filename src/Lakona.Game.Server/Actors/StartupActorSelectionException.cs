namespace Lakona.Game.Server.Actors;

public sealed class StartupActorSelectionException : Exception
{
    public StartupActorSelectionException(Type actorType, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ActorType = actorType;
    }

    public Type ActorType { get; }
}
