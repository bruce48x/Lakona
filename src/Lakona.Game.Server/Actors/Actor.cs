namespace Lakona.Game.Server.Actors;

/// <summary>
/// Base class for a process-local game actor.
/// </summary>
/// <remarks>
/// Stable actor classes normally live in the server app project and hold long-lived
/// state. Reloadable game behavior should be implemented as hotfix behavior methods
/// bound to the actor, not as mutable delegates or background work stored on the actor.
/// </remarks>
public abstract class Actor : IActor
{
    /// <summary>
    /// Gets the runtime context for the currently hosted actor instance.
    /// </summary>
    /// <remarks>
    /// The context is assigned before <see cref="OnActivateAsync"/> runs. It is not
    /// valid on an actor instance that has not been hosted by the framework.
    /// </remarks>
    public ActorContext Context { get; private set; } = ActorContext.Uninitialized;

    internal async ValueTask ActivateAsync(ActorContext context, CancellationToken cancellationToken)
    {
        Context = context;
        await OnActivateAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask DeactivateAsync(CancellationToken cancellationToken)
    {
        await OnDeactivateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs after the actor has been attached to the runtime.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels actor activation.</param>
    /// <returns>A task-like value that completes when activation work finishes.</returns>
    protected virtual ValueTask OnActivateAsync(CancellationToken cancellationToken)
    {
        return default;
    }

    /// <summary>
    /// Runs before the actor is removed from the local runtime.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels actor deactivation.</param>
    /// <returns>A task-like value that completes when deactivation work finishes.</returns>
    protected virtual ValueTask OnDeactivateAsync(CancellationToken cancellationToken)
    {
        return default;
    }
}

/// <summary>
/// Base class for an actor keyed by a strongly typed business id.
/// </summary>
/// <typeparam name="TKey">The actor key type used by generated actor selectors.</typeparam>
/// <remarks>
/// Generated actor references use <typeparamref name="TKey"/> for methods such
/// as <c>Get(id)</c>, <c>Local(id)</c>, and <c>Remote(nodeId, id)</c>. The key
/// should be a stable business identity, not a node id or connection id.
/// </remarks>
public abstract class Actor<TKey> : Actor
    where TKey : notnull
{
}
