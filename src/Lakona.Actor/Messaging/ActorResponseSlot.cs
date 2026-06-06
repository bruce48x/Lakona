namespace Lakona.Actor.Messaging;

internal sealed class ActorResponseSlot
{
    private readonly TaskCompletionSource<object?>? response;

    internal ActorResponseSlot(TaskCompletionSource<object?>? response)
    {
        this.response = response;
    }

    internal bool HasPendingResponse => response is not null;

    internal void Respond<TResponse>(TResponse responseValue)
    {
        if (!TryRespond(responseValue))
        {
            throw new InvalidOperationException("The current message does not have a pending response or was already completed.");
        }
    }

    internal bool TryRespond<TResponse>(TResponse responseValue)
    {
        return response?.TrySetResult(responseValue) == true;
    }
}
