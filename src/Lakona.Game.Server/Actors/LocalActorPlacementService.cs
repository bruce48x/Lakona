namespace Lakona.Game.Server.Actors;

internal sealed class LocalActorPlacementService(
    IActorDirectory directory,
    ActorHosting hosting,
    LocalActorNodeIdentity localNode) : IActorPlacementService
{
    public async ValueTask<ActorPlacementResult> PlaceAsync<TActor, TKey>(
        TKey key,
        ActorPlacementCreateMode createMode,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor
    {
        ArgumentNullException.ThrowIfNull(key);
        var actorId = key is ActorId id ? id : ActorId.From(key.ToString() ?? throw new ArgumentException("Actor key cannot convert to an actor id.", nameof(key)));
        var existing = await directory.ResolveAsync(actorId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (createMode == ActorPlacementCreateMode.Create)
                throw new ActorPlacementException(typeof(TActor), actorId, $"Actor id '{actorId.Value}' already has an activation owned by node '{existing.Node.Value}'.");
            return new ActorPlacementResult(existing);
        }
        if (createMode == ActorPlacementCreateMode.Create) await hosting.CreateAsync<TActor>(actorId, cancellationToken).ConfigureAwait(false);
        else await hosting.EnsureAsync<TActor>(actorId, cancellationToken).ConfigureAwait(false);
        return new ActorPlacementResult(actorId, localNode.NodeId);
    }
}
