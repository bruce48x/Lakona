using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;

namespace Lakona.Game.Server.Tests.Testing;

internal sealed class TestActorDirectory : IActorDirectory, IActorActivationDirectory
{
    private readonly object gate = new();
    private readonly Dictionary<ActorId, ActorDirectoryRecord> records = new();

    public ValueTask<ActorDirectoryRecord?> ResolveAsync(
        ActorId actorId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            records.TryGetValue(actorId, out var record);
            return new ValueTask<ActorDirectoryRecord?>(record);
        }
    }

    public ValueTask<ActorDirectoryRegisterStatus> RegisterAsync(
        ActorId actorId,
        NodeId node,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (records.TryGetValue(actorId, out var existing))
            {
                return new ValueTask<ActorDirectoryRegisterStatus>(existing.Node == node
                    ? ActorDirectoryRegisterStatus.AlreadyRegistered
                    : ActorDirectoryRegisterStatus.Conflict);
            }

            records[actorId] = new ActorDirectoryRecord(
                actorId,
                TestReference(node),
                ActorActivationId.New(),
                DateTimeOffset.UtcNow);
            return new ValueTask<ActorDirectoryRegisterStatus>(ActorDirectoryRegisterStatus.Registered);
        }
    }

    public ValueTask<ActorDirectoryUnregisterStatus> UnregisterAsync(
        ActorId actorId,
        NodeId node,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!records.TryGetValue(actorId, out var existing))
            {
                return new ValueTask<ActorDirectoryUnregisterStatus>(ActorDirectoryUnregisterStatus.NotFound);
            }

            if (existing.Node != node)
            {
                return new ValueTask<ActorDirectoryUnregisterStatus>(ActorDirectoryUnregisterStatus.OwnershipMismatch);
            }

            records.Remove(actorId);
            return new ValueTask<ActorDirectoryUnregisterStatus>(ActorDirectoryUnregisterStatus.Unregistered);
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
        lock (gate)
        {
            if (records.TryGetValue(actorId, out var existing))
            {
                return new ValueTask<ActorActivationAcquireResult>(
                    new ActorActivationAcquireResult(existing, false));
            }

            var record = new ActorDirectoryRecord(
                actorId,
                proposedOwner,
                proposedActivation,
                DateTimeOffset.UtcNow);
            records.Add(actorId, record);
            return new ValueTask<ActorActivationAcquireResult>(
                new ActorActivationAcquireResult(record, true));
        }
    }

    public ValueTask<bool> ReleaseAsync(
        ActorId actorId,
        ActorActivationId expectedActivation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!records.TryGetValue(actorId, out var existing)
                || existing.ActivationId != expectedActivation)
            {
                return new ValueTask<bool>(false);
            }

            records.Remove(actorId);
            return new ValueTask<bool>(true);
        }
    }

    private static NodeReference TestReference(NodeId node) => new(
        new ClusterIncarnationId(Guid.Parse("71000000-0000-0000-0000-000000000000")),
        node,
        new NodeIncarnationId(Guid.Parse("72000000-0000-0000-0000-000000000000")));
}
