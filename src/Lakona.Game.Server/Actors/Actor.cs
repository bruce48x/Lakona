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
    /// The context is assigned before the Actor's Hotfix <c>[ActorStart]</c>
    /// lifecycle method runs. It is not valid on an actor instance that has not
    /// been hosted by the framework.
    /// </remarks>
    public ActorContext Context { get; private set; } = ActorContext.Uninitialized;

    internal void Attach(ActorContext context)
    {
        Context = context;
    }
}

/// <summary>
/// Base class for an actor keyed by a strongly typed business id.
/// </summary>
/// <typeparam name="TKey">The actor key type used by generated actor selectors.</typeparam>
/// <remarks>
/// Generated actor references use <typeparamref name="TKey"/> for methods such
/// as <c>Local(id)</c> and <c>Route(id)</c>. The key should be a stable
/// business identity, not a node id or connection id.
/// </remarks>
public abstract class Actor<TKey> : Actor
    where TKey : notnull
{
}
