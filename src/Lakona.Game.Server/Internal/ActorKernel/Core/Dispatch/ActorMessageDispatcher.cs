using System.Diagnostics;
using Lakona.Game.Server.Internal.ActorKernel.Messaging;

namespace Lakona.Game.Server.Internal.ActorKernel.Core;

internal sealed class ActorMessageDispatcher
{
    private readonly ActorRegistry registry;
    private readonly ActorSystemDiagnosticsPublisher diagnostics;
    private readonly ActorCallDispatcher callDispatcher;
    private readonly Func<ActorCallContext?> getCurrentCallContext;

    internal ActorMessageDispatcher(
        ActorRegistry registry,
        ActorSystemDiagnosticsPublisher diagnostics,
        Func<ActorCallContext?> getCurrentCallContext)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(getCurrentCallContext);

        this.registry = registry;
        this.diagnostics = diagnostics;
        this.getCurrentCallContext = getCurrentCallContext;
        callDispatcher = new ActorCallDispatcher(diagnostics, getCurrentCallContext);
    }

    internal async ValueTask Send(ActorId target, object message, CancellationToken cancellationToken = default)
    {
        await Send(target, message, GetCurrentActivityContext(), cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask Send(
        ActorId target,
        object message,
        ActivityContext parentActivityContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        ActorCell cell = GetActorForDelivery(target, message);

        try
        {
            await cell.Send(
                new Envelope(message, callChain: GetCurrentCallChain(), parentActivityContext: parentActivityContext),
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            LakonaActorDiagnostics.MessageRejectedCounter.Add(1, new KeyValuePair<string, object?>("reason", "completed"));
            diagnostics.PublishDeadLetter(target, message, "Actor mailbox is completed.");
            throw;
        }
    }

    internal ActorSendResult TrySend(ActorId target, object message)
    {
        return TrySend(target, message, GetCurrentActivityContext());
    }

    internal ActorSendResult TrySend(
        ActorId target,
        object message,
        ActivityContext parentActivityContext)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!registry.TryGet(target, out ActorCell? cell))
        {
            LakonaActorDiagnostics.MessageRejectedCounter.Add(1, new KeyValuePair<string, object?>("reason", "unavailable"));
            diagnostics.PublishDeadLetter(target, message, "Actor does not exist.");
            return ActorSendResult.ActorUnavailable;
        }

        if (cell.IsStopping)
        {
            LakonaActorDiagnostics.MessageRejectedCounter.Add(1, new KeyValuePair<string, object?>("reason", "stopping"));
            diagnostics.PublishDeadLetter(target, message, "Actor is stopping.");
            return ActorSendResult.ActorUnavailable;
        }

        if (cell.TrySend(new Envelope(
            message,
            callChain: GetCurrentCallChain(),
            parentActivityContext: parentActivityContext)))
        {
            return ActorSendResult.Accepted;
        }

        string reason = cell.Completion.IsCompleted
            ? "Actor mailbox is completed."
            : "Actor mailbox is full.";

        LakonaActorDiagnostics.MessageRejectedCounter.Add(1, new KeyValuePair<string, object?>(
            "reason",
            cell.Completion.IsCompleted ? "completed" : "full"));
        diagnostics.PublishDeadLetter(target, message, reason);

        return cell.Completion.IsCompleted
            ? ActorSendResult.ActorUnavailable
            : ActorSendResult.MailboxFull;
    }

    internal ValueTask<TResponse> Call<TResponse>(
        ActorId target,
        object request,
        ActorCallOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ActorCell cell = GetActorForDelivery(target, request);
        return callDispatcher.Call<TResponse>(target, cell, request, options, cancellationToken);
    }

    private ActorCell GetActorForDelivery(ActorId target, object message)
    {
        if (!registry.TryGet(target, out ActorCell? cell))
        {
            LakonaActorDiagnostics.MessageRejectedCounter.Add(1, new KeyValuePair<string, object?>("reason", "unavailable"));
            diagnostics.PublishDeadLetter(target, message, "Actor does not exist.");
            throw new InvalidOperationException($"Actor {target} does not exist.");
        }

        if (cell.IsStopping)
        {
            LakonaActorDiagnostics.MessageRejectedCounter.Add(1, new KeyValuePair<string, object?>("reason", "stopping"));
            diagnostics.PublishDeadLetter(target, message, "Actor is stopping.");
            throw new InvalidOperationException($"Actor {target} is stopping.");
        }

        return cell;
    }

    private IReadOnlyList<ActorId> GetCurrentCallChain()
    {
        return getCurrentCallContext()?.CallChain ?? Array.Empty<ActorId>();
    }

    private static ActivityContext GetCurrentActivityContext()
    {
        return Activity.Current?.Context ?? default;
    }
}
