using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;

namespace Lakona.Game.Server.Tests.Testing;

internal sealed class TestActorDirectory : IActorDirectory
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
}
