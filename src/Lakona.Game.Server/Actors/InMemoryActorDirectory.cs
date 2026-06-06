using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed class InMemoryActorDirectory : IActorDirectory
{
    private readonly object _gate = new();
    private readonly Dictionary<ActorId, ActorDirectoryRecord> _records = new();
    private long _nextVersion;

    public ValueTask<ActorDirectoryRecord?> ResolveAsync(
        ActorId actorId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _records.TryGetValue(actorId, out var record);
            return ValueTask.FromResult(record);
        }
    }

    public ValueTask<ActorDirectoryRegisterStatus> RegisterAsync(
        ActorId actorId,
        NodeId node,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_records.TryGetValue(actorId, out var existing))
            {
                return ValueTask.FromResult(existing.Node == node
                    ? ActorDirectoryRegisterStatus.AlreadyRegistered
                    : ActorDirectoryRegisterStatus.Conflict);
            }

            _records[actorId] = new ActorDirectoryRecord(
                actorId,
                node,
                ++_nextVersion,
                DateTimeOffset.UtcNow);

            return ValueTask.FromResult(ActorDirectoryRegisterStatus.Registered);
        }
    }

    public ValueTask<ActorDirectoryUnregisterStatus> UnregisterAsync(
        ActorId actorId,
        NodeId node,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_records.TryGetValue(actorId, out var existing))
            {
                return ValueTask.FromResult(ActorDirectoryUnregisterStatus.NotFound);
            }

            if (existing.Node != node)
            {
                return ValueTask.FromResult(ActorDirectoryUnregisterStatus.OwnershipMismatch);
            }

            _records.Remove(actorId);
            return ValueTask.FromResult(ActorDirectoryUnregisterStatus.Unregistered);
        }
    }
}
