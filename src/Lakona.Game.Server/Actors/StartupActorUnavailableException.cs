namespace Lakona.Game.Server.Actors;

public sealed class StartupActorUnavailableException : Exception
{
    public StartupActorUnavailableException(Type actorType)
        : base($"No compatible ready Startup Actor replica is available for '{actorType.FullName ?? actorType.Name}'.")
    {
        ActorType = actorType;
    }

    public Type ActorType { get; }
}
