using System.Diagnostics;
using Lakona.Game.Server.Internal.ActorKernel.Messaging;

namespace Lakona.Game.Server.Internal.ActorKernel.Core;

internal sealed class ActorCallDispatcher
{
    private readonly ActorCallQueue callQueue;
    private readonly ActorCallResponseWaiter responseWaiter;
    private readonly Func<ActorCallContext?> getCurrentCallContext;

    internal ActorCallDispatcher(
        ActorSystemDiagnosticsPublisher diagnostics,
        Func<ActorCallContext?> getCurrentCallContext)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(getCurrentCallContext);

        callQueue = new ActorCallQueue(diagnostics);
        responseWaiter = new ActorCallResponseWaiter(diagnostics);
        this.getCurrentCallContext = getCurrentCallContext;
    }

    internal async ValueTask<TResponse> Call<TResponse>(
        ActorId target,
        ActorCell cell,
        object request,
        ActorCallOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        if (options.QueueTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "QueueTimeout must be greater than or equal to zero.");
        }

        if (options.ResponseTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "ResponseTimeout must be greater than zero.");
        }

        TaskCompletionSource<object?> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ActorCallContext? caller = getCurrentCallContext();
        IReadOnlyList<ActorId> callChain = caller?.CallChain ?? Array.Empty<ActorId>();
        long startedAt = Stopwatch.GetTimestamp();

        if (callChain.Contains(target))
        {
            throw new InvalidOperationException(
                $"Circular actor call detected. The target actor {target.Value} is already in the call chain " +
                $"({string.Join(" -> ", callChain.Select(id => id.Value.ToString()))}). " +
                "Circular calls between actors indicate a design problem. " +
                "Restructure your actors to avoid circular dependencies.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        LakonaActorDiagnostics.CallStartedCounter.Add(1);
        Envelope envelope = new(request, response, callChain, GetCurrentActivityContext());

        using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(static state =>
        {
            ((TaskCompletionSource<object?>)state!).TrySetCanceled();
        }, response);

        await callQueue.QueueCall(
            target,
            cell,
            request,
            options,
            cancellationToken,
            response,
            caller,
            callChain,
            startedAt,
            envelope).ConfigureAwait(false);

        return await responseWaiter.WaitForResponse<TResponse>(
            target,
            request,
            options,
            cancellationToken,
            response,
            caller,
            callChain,
            startedAt).ConfigureAwait(false);
    }

    private static ActivityContext GetCurrentActivityContext()
    {
        return Activity.Current?.Context ?? default;
    }
}
