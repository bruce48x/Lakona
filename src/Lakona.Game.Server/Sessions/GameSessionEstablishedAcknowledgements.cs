using System.Collections.Concurrent;

namespace Lakona.Game.Server.Sessions;

internal sealed class GameSessionEstablishedAcknowledgements
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> pending =
        new(StringComparer.Ordinal);

    public ValueTask WaitAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pending.TryAdd(connectionId, completion))
        {
            throw new InvalidOperationException("A Game Session establishment acknowledgement is already pending.");
        }

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(static state =>
            {
                var registration = (CancellationRegistration)state!;
                if (registration.Owner.pending.TryRemove(
                    new KeyValuePair<string, TaskCompletionSource<bool>>(registration.ConnectionId, registration.Completion)))
                {
                    registration.Completion.TrySetCanceled(registration.CancellationToken);
                }
            }, new CancellationRegistration(this, connectionId, completion, cancellationToken));
        }

        return new ValueTask(completion.Task);
    }

    public bool Acknowledge(string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        if (!pending.TryRemove(connectionId, out var completion))
        {
            return false;
        }

        return completion.TrySetResult(true);
    }

    public void Cancel(string connectionId)
    {
        if (pending.TryRemove(connectionId, out var completion))
        {
            completion.TrySetCanceled();
        }
    }

    private sealed record CancellationRegistration(
        GameSessionEstablishedAcknowledgements Owner,
        string ConnectionId,
        TaskCompletionSource<bool> Completion,
        CancellationToken CancellationToken);
}
