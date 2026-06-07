namespace Lakona.Game.Server.Internal.ActorKernel;

internal readonly record struct ActorId(long Value)
{
    public override string ToString() => Value.ToString();
}
