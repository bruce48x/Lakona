namespace Lakona.Game.Server.Actors;

public readonly record struct ActorActivationId(Guid Value)
{
    public static ActorActivationId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}
