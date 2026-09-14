namespace Lakona.Game.Server.Actors;

/// <summary>A stable, exact mailbox identity; never resolves a replacement by ActorId.</summary>
internal sealed class ActorTimerOwner(
    Func<Internal.ActorWorkItem, ValueTask> dispatch,
    Func<bool> isCurrentTurn)
{
    private readonly CancellationTokenSource stopping = new();
    internal CancellationToken Stopping => stopping.Token;

    internal void ValidateCreation()
    {
        stopping.Token.ThrowIfCancellationRequested();
        ValidateTurn();
    }

    internal void ValidateTurn()
    {
        if (!isCurrentTurn())
            throw new InvalidOperationException("Actor timers must be accessed in their owner's active turn.");
    }

    internal void Stop() => stopping.Cancel();

    internal ValueTask InvokeAsync(
        Func<IActor, CancellationToken, ValueTask> callback,
        CancellationToken cancellationToken) =>
        dispatch(new Internal.ActorWorkItem(
            static async (actor, state, ct) =>
            {
                await ((Func<IActor, CancellationToken, ValueTask>)state)(actor, ct).ConfigureAwait(false);
                return null;
            }, callback, cancellationToken));
}
