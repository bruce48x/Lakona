using Lakona.Game.Server.Actors;

namespace Lakona.Game.Server.Hotfix;

/// <summary>
/// Provides cluster-aware lifecycle operations for one logical actor identity.
/// </summary>
/// <typeparam name="TActor">The actor implementation type.</typeparam>
/// <typeparam name="TKey">The actor's stable business-key type.</typeparam>
public readonly struct ActorPlacement<TActor, TKey>
    where TActor : Actor<TKey>
    where TKey : notnull
{
    private readonly IActorPlacementService _placement;
    private readonly TKey _id;

    /// <summary>
    /// Initializes a placement selector for one logical actor identity.
    /// </summary>
    /// <param name="placement">The cluster-aware placement service.</param>
    /// <param name="id">The actor's stable business key.</param>
    public ActorPlacement(IActorPlacementService placement, TKey id)
    {
        _placement = placement ?? throw new ArgumentNullException(nameof(placement));
        _id = id;
    }

    /// <summary>
    /// Creates a new activation and fails when the logical actor already has an activation.
    /// </summary>
    /// <param name="cancellationToken">Cancels placement and activation.</param>
    /// <returns>The newly created activation and its owner.</returns>
    public ValueTask<ActorPlacementResult> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        return _placement.PlaceAsync<TActor, TKey>(
            ActorIdentity.Create<TActor, TKey>(_id),
            _id,
            ActorPlacementCreateMode.Create,
            cancellationToken);
    }

    /// <summary>
    /// Returns the existing activation or creates one when the logical actor is absent.
    /// </summary>
    /// <param name="cancellationToken">Cancels placement and activation.</param>
    /// <returns>The existing or newly created activation and its owner.</returns>
    public ValueTask<ActorPlacementResult> EnsureAsync(
        CancellationToken cancellationToken = default)
    {
        return _placement.PlaceAsync<TActor, TKey>(
            ActorIdentity.Create<TActor, TKey>(_id),
            _id,
            ActorPlacementCreateMode.Ensure,
            cancellationToken);
    }

    /// <summary>
    /// Destroys the activation that is current when this operation resolves it.
    /// </summary>
    /// <param name="cancellationToken">Cancels location lookup and actor retirement.</param>
    public ValueTask DestroyAsync(
        CancellationToken cancellationToken = default)
    {
        return _placement.DestroyAsync<TActor>(
            ActorIdentity.Create<TActor, TKey>(_id),
            cancellationToken);
    }
}
