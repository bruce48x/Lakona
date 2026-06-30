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
    ValueTask<TActor> GetOrCreateAsync<TActor>(
        ActorId id,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor;

    ValueTask TellAsync<TActor>(
        ActorId id,
        Func<TActor, CancellationToken, ValueTask> message,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor;

    ActorTellResult TryTell<TActor>(
        ActorId id,
        Func<TActor, CancellationToken, ValueTask> message,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor;

    ValueTask TellAsync(
        Type actorType,
        ActorId id,
        Func<IActor, CancellationToken, ValueTask> message,
        CancellationToken cancellationToken = default);

    ActorTellResult TryTell(
        Type actorType,
        ActorId id,
        Func<IActor, CancellationToken, ValueTask> message,
        CancellationToken cancellationToken = default);

    ValueTask<TResult> AskAsync<TActor, TResult>(
        ActorId id,
        Func<TActor, CancellationToken, ValueTask<TResult>> message,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor;

    ActorRuntimeDiagnosticsSnapshot GetDiagnosticsSnapshot()
    {
        return new ActorRuntimeDiagnosticsSnapshot([]);
    }

    IReadOnlyList<ActorId> GetActiveActorIds(Type actorType);

    IAsyncDisposable RegisterTimer<TActor>(
        ActorId id,
        TimeSpan dueTime,
        TimeSpan? period,
        Func<TActor, CancellationToken, ValueTask> callback)
        where TActor : class, IActor;

    bool TryGetMailboxMetrics(ActorId id, out ActorMailboxMetrics metrics);

    ActorState GetState(ActorId id);

    ValueTask StopAsync(ActorId id);

    ValueTask<ActorStopOutcome> StopAsync(ActorId id, TimeSpan drainTimeout);
}
