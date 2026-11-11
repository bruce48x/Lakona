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
    public static async ValueTask TellAsync<TActor, TRequest>(
        IActorRuntime runtime,
        ActorId actorId,
        IHotfixRuntimeAccessor runtimeAccessor,
        ulong methodId,
        TRequest request,
        IDistributedWorkAdmissionGate? admissionGate,
        CancellationToken cancellationToken)
        where TActor : class, IActor
    {
        if (admissionGate is null)
        {
            await runtime.TellAsync<TActor>(actorId,
                (actor, ct) => HotfixDispatch.InvokeActorAsync(runtimeAccessor, methodId, actor, request, ct),
                cancellationToken).ConfigureAwait(false);
            return;
        }
        var catalog = RequireAdmissionRuntime(runtime);
        var admission = Enter(admissionGate);
        try
        {
            await catalog.TellAsync<TActor>(actorId, async (actor, ct) =>
            {
                try { await HotfixDispatch.InvokeActorAsync(runtimeAccessor, methodId, actor, request, ct).ConfigureAwait(false); }
                finally { admission.Complete(); }
            }, cancellationToken, admission.Accept).ConfigureAwait(false);
        }
        finally { admission.CompleteIfRejected(); }
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

        var accepted = false;
        try
        {
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
            accepted = result == ActorTellResult.Accepted;
            return result;
        }
        finally
        {
            // Until the mailbox accepts ownership, every exit belongs to the caller.
            if (!accepted && admission.IsAdmitted) admissionGate!.Exit(admission);
        }
    }

    public static async ValueTask<TResult> AskAsync<TActor, TRequest, TResult>(
        IActorRuntime runtime,
        ActorId actorId,
        IHotfixRuntimeAccessor runtimeAccessor,
        ulong methodId,
        TRequest request,
        IDistributedWorkAdmissionGate? admissionGate,
        CancellationToken cancellationToken)
        where TActor : class, IActor
    {
        if (admissionGate is null)
        {
            return await runtime.AskAsync<TActor, TResult>(actorId,
                (actor, ct) => HotfixDispatch.InvokeActorAsync<TResult>(runtimeAccessor, methodId, actor, request, ct),
                cancellationToken).ConfigureAwait(false);
        }
        var catalog = RequireAdmissionRuntime(runtime);
        var admission = Enter(admissionGate);
        try
        {
            return await catalog.AskAsync<TActor, TResult>(actorId, async (actor, ct) =>
            {
                try { return await HotfixDispatch.InvokeActorAsync<TResult>(runtimeAccessor, methodId, actor, request, ct).ConfigureAwait(false); }
                finally { admission.Complete(); }
            }, cancellationToken, admission.Accept).ConfigureAwait(false);
        }
        finally { admission.CompleteIfRejected(); }
    }

    private static ActorActivationCatalog RequireAdmissionRuntime(IActorRuntime runtime) =>
        runtime as ActorActivationCatalog ?? throw new NotSupportedException(
            "Distributed Actor calls require the framework runtime's mailbox acceptance tracking.");

    private static ActorWorkAdmission Enter(IDistributedWorkAdmissionGate gate)
    {
        if (!gate.TryEnter(out var admission))
        {
            throw ActorNotFoundException.BeforeDispatch(default, typeof(IActor).FullName!, "admission", "Distributed Actor admission is closed.");
        }
        return new ActorWorkAdmission(gate, admission);
    }
}
