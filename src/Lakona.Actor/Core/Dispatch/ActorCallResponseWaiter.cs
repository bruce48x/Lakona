using System.Diagnostics;
using Lakona.Actor.Messaging;

namespace Lakona.Actor.Core;

internal sealed class ActorCallResponseWaiter
{
    private readonly ActorSystemDiagnosticsPublisher diagnostics;

    internal ActorCallResponseWaiter(ActorSystemDiagnosticsPublisher diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        this.diagnostics = diagnostics;
    }

    internal async ValueTask<TResponse> WaitForResponse<TResponse>(
        ActorId target,
        object request,
        ActorCallOptions options,
        CancellationToken cancellationToken,
        TaskCompletionSource<object?> response,
        ActorCallContext? caller,
        IReadOnlyList<ActorId> callChain,
        long startedAt)
    {
        using CancellationTokenSource responseTimeoutCts = new(options.ResponseTimeout);
        using CancellationTokenSource linkedResponseCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            responseTimeoutCts.Token);

        object? result;

        try
        {
            result = await response.Task.WaitAsync(linkedResponseCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            responseTimeoutCts.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            ActorCallTimeout timeoutDiagnostic = diagnostics.PublishCallTimeout(
                caller?.ActorId,
                target,
                request,
                options,
                Stopwatch.GetElapsedTime(startedAt),
                ActorCallTimeoutReason.ResponseTimeout,
                callChain);
            TimeoutException exception = diagnostics.CreateCallTimeoutException(timeoutDiagnostic, "The actor call timed out.");
            response.TrySetException(exception);
            throw exception;
        }

        if (result is null)
        {
            return default!;
        }

        if (result is TResponse typed)
        {
            return typed;
        }

        throw new InvalidCastException($"Actor responded with {result.GetType().FullName}, not {typeof(TResponse).FullName}.");
    }
}
