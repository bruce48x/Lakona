namespace Lakona.Game.Server.Internal.ActorKernel.Messaging;

internal sealed class ActorContextCore
{
    private readonly ActorResponseSlot responseSlot;

    internal ActorContextCore(ActorRef self, Envelope envelope)
    {
        Self = self;
        responseSlot = new ActorResponseSlot(envelope.Response);
    }

    internal ActorRef Self { get; }

    internal bool HasPendingResponse => responseSlot.HasPendingResponse;

    public void Respond<TResponse>(TResponse response)
    {
        responseSlot.Respond(response);
    }

    public bool TryRespond<TResponse>(TResponse response)
    {
        return responseSlot.TryRespond(response);
    }
}
