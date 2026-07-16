using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Dispatch;

namespace Lakona.Game.Server.Actors;

/// <summary>
/// Creates actor-mailbox delegates in the stable server assembly so queued
/// work never retains executable code from a retired hotfix assembly.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class HotfixActorMailboxDispatch
{
    public static ValueTask TellAsync<TActor, TRequest>(
        IActorRuntime runtime,
        ActorId actorId,
        IHotfixRuntimeAccessor runtimeAccessor,
        ulong methodId,
        TRequest request,
        CancellationToken cancellationToken)
        where TActor : class, IActor
    {
        return runtime.TellAsync<TActor>(
            actorId,
            (actor, executionToken) => HotfixDispatch.InvokeActorAsync(
                runtimeAccessor,
                methodId,
                actor,
                request,
                executionToken),
            cancellationToken);
    }

    public static ActorTellResult TryTell<TActor, TRequest>(
        IActorRuntime runtime,
        ActorId actorId,
        IHotfixRuntimeAccessor runtimeAccessor,
        ulong methodId,
        TRequest request,
        CancellationToken cancellationToken)
        where TActor : class, IActor
    {
        return runtime.TryTell<TActor>(
            actorId,
            (actor, executionToken) => HotfixDispatch.InvokeActorAsync(
                runtimeAccessor,
                methodId,
                actor,
                request,
                executionToken),
            cancellationToken);
    }

    public static ValueTask<TResult> AskAsync<TActor, TRequest, TResult>(
        IActorRuntime runtime,
        ActorId actorId,
        IHotfixRuntimeAccessor runtimeAccessor,
        ulong methodId,
        TRequest request,
        CancellationToken cancellationToken)
        where TActor : class, IActor
    {
        return runtime.AskAsync<TActor, TResult>(
            actorId,
            (actor, executionToken) => HotfixDispatch.InvokeActorAsync<TResult>(
                runtimeAccessor,
                methodId,
                actor,
                request,
                executionToken),
            cancellationToken);
    }
}
