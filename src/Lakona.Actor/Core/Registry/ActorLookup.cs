using System.Diagnostics.CodeAnalysis;
using Lakona.Actor.Messaging;

namespace Lakona.Actor.Core;

internal sealed class ActorLookup
{
    private readonly ActorSystem system;
    private readonly ActorRegistry registry;

    internal ActorLookup(ActorSystem system, ActorRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(registry);

        this.system = system;
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

    internal bool TryGetActor(string name, out ActorRef? actorRef)
    {
        if (registry.TryGetNamed(name, out ActorId id))
        {
            actorRef = new ActorRef(system, id);
            return true;
        }

        actorRef = null;
        return false;
    }

    internal bool TryGetActor(ActorId target, [NotNullWhen(true)] out ActorCell? cell)
    {
        return registry.TryGet(target, out cell);
    }

    internal bool TryGetActor(string name, Type messageType, out ActorRef? actorRef)
    {
        if (registry.TryGetNamed(name, out ActorId id, out ActorCell? cell))
        {
            if (cell.MessageType != messageType)
            {
                throw new InvalidOperationException(
                    $"Actor name '{name}' was registered for message type {cell.MessageType.FullName}, not {messageType.FullName}.");
            }

            actorRef = new ActorRef(system, id);
            return true;
        }

        actorRef = null;
        return false;
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
