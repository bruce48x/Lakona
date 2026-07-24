namespace Lakona.Game.Server.Internal.ActorKernel.Core;

internal sealed class ActorLookup
{
    private readonly ActorRegistry registry;

    internal ActorLookup(ActorRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        this.registry = registry;
    }

    internal MailboxMetrics GetMailboxMetrics(ActorId target)
    {
        ActorCell cell = GetActor(target);
        return cell.GetMailboxMetrics();
    }

    internal ActorState GetActorState(ActorId target)
    {
        if (!registry.TryGet(target, out ActorCell? cell))
        {
            return ActorState.Dead;
        }

        return cell.State;
    }

    private ActorCell GetActor(ActorId target)
    {
        if (!registry.TryGet(target, out ActorCell? cell))
        {
            throw new InvalidOperationException($"Actor {target} does not exist.");
        }

        return cell;
    }
}
