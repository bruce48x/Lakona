using System.Diagnostics;

namespace Lakona.Game.Server.Actors.Internal;

internal sealed record ActorWorkItem(
    Func<IActor, object, CancellationToken, ValueTask<object?>> Callback,
    object State,
    CancellationToken CancellationToken)
{
    internal string MessageType => State.GetType().FullName ?? State.GetType().Name;
}

internal sealed record ActorMailboxEntry(
    ActorWorkItem Work,
    TaskCompletionSource<object?>? Response,
    IReadOnlyList<ActorId> CallChain,
    ActivityContext ParentActivityContext);

internal sealed class ActorCallContext
{
    private int _active = 1;

    internal ActorCallContext(ActorId actorId, IReadOnlyList<ActorId> callChain)
    {
        ActorId = actorId;
        CallChain = callChain;
    }

    internal ActorId ActorId { get; }

    internal IReadOnlyList<ActorId> CallChain { get; }

    internal bool IsActive => Volatile.Read(ref _active) != 0;

    internal void Deactivate()
    {
        Volatile.Write(ref _active, 0);
    }
}

internal enum ActorMailboxStopResult
{
    Stopped,
    TimedOut
}
