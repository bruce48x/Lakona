using System.Diagnostics;

namespace Lakona.Game.Server.Internal.ActorKernel.Messaging;

internal sealed class Envelope
{
    public Envelope(
        object message,
        TaskCompletionSource<object?>? response = null,
        IReadOnlyList<ActorId>? callChain = null,
        ActivityContext parentActivityContext = default)
    {
        Message = message;
        Response = response;
        CallChain = callChain ?? Array.Empty<ActorId>();
        ParentActivityContext = parentActivityContext;
    }

    public object Message { get; }

    public TaskCompletionSource<object?>? Response { get; }

    public IReadOnlyList<ActorId> CallChain { get; }

    public ActivityContext ParentActivityContext { get; }
}
