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

    internal async ValueTask<ActorRef> SpawnAsync(
        IActor actor,
        Type messageType,
        ActorSpawnOptions? spawnOptions,
        string? name)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(messageType);

        int mailboxCapacity = spawnOptions?.MailboxCapacity ?? options.MailboxCapacity;

        if (mailboxCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spawnOptions), "MailboxCapacity must be greater than zero.");
        }

        ActorId id = new(Interlocked.Increment(ref nextActorId));
        ActorRef actorRef = new(system, id);
        ActorCell cell = new(system, actorRef, actor, messageType, mailboxCapacity, options.SlowMessageThreshold, name);

        if (!registry.TryAdd(id, cell))
        {
            throw new InvalidOperationException($"Actor id {id} already exists.");
        }

        if (name is not null && !registry.TryAddName(name, id))
        {
            registry.Remove(id, cell);
            cell.Complete();
            throw new InvalidOperationException($"Actor name '{name}' already exists.");
        }

        try
        {
            await cell.StartAsync().ConfigureAwait(false);
        }
        catch
        {
            registry.Remove(id, cell);
            cell.Complete();
            throw;
        }

        return actorRef;
    }
}
