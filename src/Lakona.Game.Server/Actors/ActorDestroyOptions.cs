namespace Lakona.Game.Server.Actors;

public sealed class ActorDestroyOptions
{
    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
