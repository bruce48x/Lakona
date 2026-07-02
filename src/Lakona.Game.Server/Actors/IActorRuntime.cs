namespace Lakona.Game.Server.Actors;

/// <summary>
/// Process-local actor runtime used by generated actor references and framework
/// boundary services.
/// </summary>
/// <remarks>
/// This API is public so generated code can enter local actor mailboxes from
/// user assemblies. Application business code should prefer generated actor
/// selectors, such as <c>Get(id)</c>, <c>Local(id)</c>, and
/// <c>Remote(nodeId, id)</c>, because those selectors make actor placement
/// intent explicit.
/// </remarks>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public interface IActorRuntime
{
    /// <summary>
    /// Enqueues a fire-and-forget message for a local actor.
    /// </summary>
    /// <typeparam name="TActor">The expected actor type.</typeparam>
    /// <param name="id">The target actor id.</param>
    /// <param name="message">The delegate executed inside the actor turn.</param>
    /// <param name="cancellationToken">A token that cancels enqueueing before the message is accepted.</param>
    ValueTask TellAsync<TActor>(
        ActorId id,
        Func<TActor, CancellationToken, ValueTask> message,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor;

    /// <summary>
    /// Attempts to enqueue a fire-and-forget message without throwing for common admission failures.
    /// </summary>
    /// <typeparam name="TActor">The expected actor type.</typeparam>
    /// <param name="id">The target actor id.</param>
    /// <param name="message">The delegate executed inside the actor turn when accepted.</param>
    /// <param name="cancellationToken">A token that cancels enqueueing before the message is accepted.</param>
    /// <returns>The enqueue result.</returns>
    ActorTellResult TryTell<TActor>(
        ActorId id,
        Func<TActor, CancellationToken, ValueTask> message,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor;

    /// <summary>
    /// Enqueues a fire-and-forget message for a local actor selected by runtime type.
    /// </summary>
    /// <param name="actorType">The expected actor implementation type.</param>
    /// <param name="id">The target actor id.</param>
    /// <param name="message">The delegate executed inside the actor turn.</param>
    /// <param name="cancellationToken">A token that cancels enqueueing before the message is accepted.</param>
    ValueTask TellAsync(
        Type actorType,
        ActorId id,
        Func<IActor, CancellationToken, ValueTask> message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to enqueue a fire-and-forget message for a local actor selected by runtime type.
    /// </summary>
    /// <param name="actorType">The expected actor implementation type.</param>
    /// <param name="id">The target actor id.</param>
    /// <param name="message">The delegate executed inside the actor turn when accepted.</param>
    /// <param name="cancellationToken">A token that cancels enqueueing before the message is accepted.</param>
    /// <returns>The enqueue result.</returns>
    ActorTellResult TryTell(
        Type actorType,
        ActorId id,
        Func<IActor, CancellationToken, ValueTask> message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a request/reply actor call inside the local actor turn.
    /// </summary>
    /// <typeparam name="TActor">The expected actor type.</typeparam>
    /// <typeparam name="TResult">The reply type returned by the actor call.</typeparam>
    /// <param name="id">The target actor id.</param>
    /// <param name="message">The delegate executed inside the actor turn.</param>
    /// <param name="cancellationToken">A token that cancels enqueueing or waiting for the reply.</param>
    /// <returns>The actor call reply.</returns>
    ValueTask<TResult> AskAsync<TActor, TResult>(
        ActorId id,
        Func<TActor, CancellationToken, ValueTask<TResult>> message,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor;

    /// <summary>
    /// Captures aggregate diagnostics for the local actor runtime.
    /// </summary>
    /// <returns>The actor runtime diagnostics snapshot.</returns>
    ActorRuntimeDiagnosticsSnapshot GetDiagnosticsSnapshot()
    {
        return new ActorRuntimeDiagnosticsSnapshot([]);
    }

    /// <summary>
    /// Gets active local actor ids for an actor implementation type.
    /// </summary>
    /// <param name="actorType">The actor implementation type.</param>
    /// <returns>The active actor ids.</returns>
    IReadOnlyList<ActorId> GetActiveActorIds(Type actorType);

    /// <summary>
    /// Attempts to read mailbox metrics for one local actor.
    /// </summary>
    /// <param name="id">The actor id.</param>
    /// <param name="metrics">The mailbox metrics when available.</param>
    /// <returns><see langword="true"/> when metrics were found; otherwise, <see langword="false"/>.</returns>
    bool TryGetMailboxMetrics(ActorId id, out ActorMailboxMetrics metrics);

    /// <summary>
    /// Gets the local lifecycle state for one actor.
    /// </summary>
    /// <param name="id">The actor id.</param>
    /// <returns>The actor lifecycle state.</returns>
    ActorState GetState(ActorId id);
}
