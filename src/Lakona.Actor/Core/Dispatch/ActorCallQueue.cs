using System.Diagnostics;
using Lakona.Actor.Messaging;

namespace Lakona.Actor.Core;

internal sealed class ActorCallQueue
{
    private readonly ActorSystemDiagnosticsPublisher diagnostics;

    internal ActorCallQueue(ActorSystemDiagnosticsPublisher diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        this.diagnostics = diagnostics;
    }

    internal async ValueTask QueueCall(
        ActorId target,
        ActorCell cell,
        object request,
        ActorCallOptions options,
        CancellationToken cancellationToken,
        TaskCompletionSource<object?> response,
        ActorCallContext? caller,
        IReadOnlyList<ActorId> callChain,
        long startedAt,
        Envelope envelope)
    {
        if (options.QueueTimeout == TimeSpan.Zero)
        {
            if (!cell.TrySend(envelope))
            {
                if (cell.Completion.IsCompleted)
                {
                    LakonaActorDiagnostics.MessageRejectedCounter.Add(1, new KeyValuePair<string, object?>("reason", "completed"));
                    diagnostics.PublishDeadLetter(target, request, "Actor mailbox is completed.");
                    throw new InvalidOperationException($"Actor {target} mailbox is completed.");
                }

                TimeoutException exception = PublishQueueTimeout(
                    caller,
                    target,
                    request,
                    options,
                    Stopwatch.GetElapsedTime(startedAt),
                    callChain);
                response.TrySetException(exception);
                throw exception;
            }

            return;
        }

        using CancellationTokenSource queueTimeoutCts = new(options.QueueTimeout);
        using CancellationTokenSource linkedQueueCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            queueTimeoutCts.Token);

        try
        {
            await cell.Send(envelope, linkedQueueCts.Token).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            LakonaActorDiagnostics.MessageRejectedCounter.Add(1, new KeyValuePair<string, object?>("reason", "completed"));
            diagnostics.PublishDeadLetter(target, request, "Actor mailbox is completed.");
            throw;
        }
        catch (OperationCanceledException) when (
            queueTimeoutCts.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            TimeoutException exception = PublishQueueTimeout(
                caller,
                target,
                request,
                options,
                Stopwatch.GetElapsedTime(startedAt),
                callChain);
            response.TrySetException(exception);
            throw exception;
        }
    }

    private TimeoutException PublishQueueTimeout(
        ActorCallContext? caller,
        ActorId target,
        object request,
        ActorCallOptions options,
        TimeSpan elapsed,
        IReadOnlyList<ActorId> callChain)
    {
        ActorCallTimeout timeoutDiagnostic = diagnostics.PublishCallTimeout(
            caller?.ActorId,
            target,
            request,
            options,
            elapsed,
            ActorCallTimeoutReason.QueueTimeout,
            callChain);
        return diagnostics.CreateCallTimeoutException(
            timeoutDiagnostic,
            "The actor call timed out before it could be queued.");
    }
}
