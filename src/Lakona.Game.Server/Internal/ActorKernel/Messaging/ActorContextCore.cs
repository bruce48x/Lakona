namespace Lakona.Game.Server.Internal.ActorKernel.Messaging;

internal sealed class ActorContextCore
{
    private readonly ActorResponseSlot responseSlot;

    internal ActorContextCore(Envelope envelope)
    {
        responseSlot = new ActorResponseSlot(envelope.Response);
    }

    internal bool HasPendingResponse => responseSlot.HasPendingResponse;

    public void Respond<TResponse>(TResponse response)
    {
        responseSlot.Respond(response);
    }

}
