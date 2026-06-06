namespace Lakona.Actor.Messaging;

internal sealed class ActorCallContext
{
    private int active = 1;

    public ActorCallContext(ActorId actorId, IReadOnlyList<ActorId> callChain)
    {
        ActorId = actorId;
        CallChain = callChain;
    }

    public ActorId ActorId { get; }

    public IReadOnlyList<ActorId> CallChain { get; }

    public bool IsActive => Volatile.Read(ref active) != 0;

    public void Deactivate()
    {
        Volatile.Write(ref active, 0);
    }
}
