using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors.Internal;

namespace Lakona.Game.Server.Actors;

public sealed class InMemoryActorDirectory :
    IActorDirectory,
    IActorActivationDirectory,
    IActorActivationPopulationSource
{
    private readonly object _gate = new();
    private readonly Dictionary<ActorId, ActorDirectoryRecord> _records = new();
    private readonly Dictionary<ActorId, long> _versions = new();

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

            var version = NextVersion(actorId);
            _records[actorId] = new ActorDirectoryRecord(
                actorId,
                node,
                version,
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

    public ValueTask<ActorActivationAcquireResult> AcquireAsync(
        ActorId actorId,
        NodeReference proposedOwner,
        ActorActivationId proposedActivation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposedOwner);
        cancellationToken.ThrowIfCancellationRequested();
        if (proposedActivation.Value == Guid.Empty)
        {
            throw new ArgumentException("Actor activation id is required.", nameof(proposedActivation));
        }

        lock (_gate)
        {
            if (_records.TryGetValue(actorId, out var existing))
            {
                return ValueTask.FromResult(new ActorActivationAcquireResult(existing, false));
            }

            var record = new ActorDirectoryRecord(
                actorId,
                proposedOwner,
                proposedActivation,
                NextVersion(actorId),
                DateTimeOffset.UtcNow);
            _records.Add(actorId, record);
            return ValueTask.FromResult(new ActorActivationAcquireResult(record, true));
        }
    }

    private long NextVersion(ActorId actorId)
    {
        _versions.TryGetValue(actorId, out var previous);
        if (previous == long.MaxValue)
        {
            throw new InvalidOperationException("Actor activation version is exhausted.");
        }

        var next = previous + 1;
        _versions[actorId] = next;
        return next;
    }

    public ValueTask<bool> ReleaseAsync(
        ActorId actorId,
        ActorActivationId expectedActivation,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_records.TryGetValue(actorId, out var existing)
                || existing.ActivationId != expectedActivation
                || existing.Version != expectedVersion)
            {
                return ValueTask.FromResult(false);
            }

            _records.Remove(actorId);
            return ValueTask.FromResult(true);
        }
    }

    internal ActorDirectoryRecord ApplyReplica(ActorDirectoryRecord incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        lock (_gate)
        {
            if (_records.TryGetValue(incoming.ActorId, out var existing))
            {
                if (existing.Version > incoming.Version)
                {
                    return existing;
                }

                if (existing.Version == incoming.Version)
                {
                    if (existing.OwnerReference != incoming.OwnerReference
                        || existing.ActivationId != incoming.ActivationId)
                    {
                        throw new InvalidOperationException(
                            $"Actor activation version {incoming.Version} conflicts for '{incoming.ActorId.Value}'.");
                    }

                    return existing;
                }
            }

            _records[incoming.ActorId] = incoming;
            _versions.TryGetValue(incoming.ActorId, out var previous);
            _versions[incoming.ActorId] = Math.Max(previous, incoming.Version);
            return incoming;
        }
    }

    ActorActivationPopulation IActorActivationPopulationSource.ObserveActivationPopulation()
    {
        lock (_gate)
        {
            return new ActorActivationPopulation(
                _records.Count,
                _versions.Count,
                _versions.Count - _records.Count);
        }
    }
}
