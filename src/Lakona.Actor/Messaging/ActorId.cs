namespace Lakona.Actor;

public readonly record struct ActorId(long Value)
{
    public override string ToString() => Value.ToString();
}
