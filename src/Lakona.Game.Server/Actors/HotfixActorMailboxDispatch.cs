using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hosting;

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
        IDistributedWorkAdmissionGate? admissionGate,
        CancellationToken cancellationToken)
        where TActor : class, IActor
    {
        return ExecuteAdmittedAsync(admissionGate, () => runtime.TellAsync<TActor>(
            actorId,
            (actor, executionToken) => HotfixDispatch.InvokeActorAsync(
                runtimeAccessor,
                methodId,
                actor,
                request,
                executionToken),
            cancellationToken));
    }

    public static ActorTellResult TryTell<TActor, TRequest>(
        IActorRuntime runtime,
        ActorId actorId,
        IHotfixRuntimeAccessor runtimeAccessor,
        ulong methodId,
        TRequest request,
        IDistributedWorkAdmissionGate? admissionGate,
        CancellationToken cancellationToken)
        where TActor : class, IActor
    {
        DistributedWorkAdmission admission = default;
        if (admissionGate is not null && !admissionGate.TryEnter(out admission))
        {
            return ActorTellResult.ActorUnavailable;
        }

        var result = runtime.TryTell<TActor>(
            actorId,
            async (actor, executionToken) =>
            {
                try
                {
                    await HotfixDispatch.InvokeActorAsync(
                        runtimeAccessor,
                        methodId,
                        actor,
                        request,
                        executionToken).ConfigureAwait(false);
                }
                finally
                {
                    if (admission.IsAdmitted) admissionGate!.Exit(admission);
                }
            },
            cancellationToken);
        if (result != ActorTellResult.Accepted && admission.IsAdmitted)
        {
            admissionGate!.Exit(admission);
        }

        return result;
    }

    public static ValueTask<TResult> AskAsync<TActor, TRequest, TResult>(
        IActorRuntime runtime,
        ActorId actorId,
        IHotfixRuntimeAccessor runtimeAccessor,
        ulong methodId,
        TRequest request,
        IDistributedWorkAdmissionGate? admissionGate,
        CancellationToken cancellationToken)
        where TActor : class, IActor
    {
        return ExecuteAdmittedAsync(admissionGate, () => runtime.AskAsync<TActor, TResult>(
            actorId,
            (actor, executionToken) => HotfixDispatch.InvokeActorAsync<TResult>(
                runtimeAccessor,
                methodId,
                actor,
                request,
                executionToken),
            cancellationToken));
    }

    private static async ValueTask ExecuteAdmittedAsync(
        IDistributedWorkAdmissionGate? gate,
        Func<ValueTask> operation)
    {
        DistributedWorkAdmission admission = default;
        if (gate is not null && !gate.TryEnter(out admission))
        {
            throw ActorNotFoundException.BeforeDispatch(default, typeof(IActor).FullName!, "admission", "Distributed Actor admission is closed.");
        }

        try { await operation().ConfigureAwait(false); }
        finally { if (admission.IsAdmitted) gate!.Exit(admission); }
    }

    private static async ValueTask<TResult> ExecuteAdmittedAsync<TResult>(
        IDistributedWorkAdmissionGate? gate,
        Func<ValueTask<TResult>> operation)
    {
        DistributedWorkAdmission admission = default;
        if (gate is not null && !gate.TryEnter(out admission))
        {
            throw ActorNotFoundException.BeforeDispatch(default, typeof(IActor).FullName!, "admission", "Distributed Actor admission is closed.");
        }

        try { return await operation().ConfigureAwait(false); }
        finally { if (admission.IsAdmitted) gate!.Exit(admission); }
    }
}
