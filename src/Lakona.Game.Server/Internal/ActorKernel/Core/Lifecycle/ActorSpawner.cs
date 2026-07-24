using Lakona.Game.Server.Internal.ActorKernel.Abstractions;
using Lakona.Game.Server.Internal.ActorKernel.Messaging;

namespace Lakona.Game.Server.Internal.ActorKernel.Core;

internal sealed class ActorSpawner
{
    private readonly ActorSystem system;
    private readonly ActorRegistry registry;
    private readonly ActorSystemOptions options;
    private long nextActorId;

    internal ActorSpawner(ActorSystem system, ActorRegistry registry, ActorSystemOptions options)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);

        this.system = system;
        this.registry = registry;
        this.options = options;
    }

    internal ActorRef Spawn(IActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        ActorId id = new(Interlocked.Increment(ref nextActorId));
        ActorRef actorRef = new(system, id);
        ActorCell cell = new(system, actorRef, actor, options.MailboxCapacity, options.SlowMessageThreshold);

        if (!registry.TryAdd(id, cell))
        {
            throw new InvalidOperationException($"Actor id {id} already exists.");
        }

        return actorRef;
    }
}
