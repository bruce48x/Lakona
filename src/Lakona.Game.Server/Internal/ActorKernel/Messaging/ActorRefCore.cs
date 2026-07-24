namespace Lakona.Game.Server.Internal.ActorKernel.Messaging;

internal sealed class ActorRef
{
    private readonly ActorSystem system;

    internal ActorRef(ActorSystem system, ActorId id)
    {
        this.system = system;
        Id = id;
    }

    public ActorId Id { get; }

    public ActorSendResult TrySend(object message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return system.TrySend(Id, message);
    }

    public ValueTask<TResponse> Call<TResponse>(
        object request,
        ActorCallOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return system.Call<TResponse>(Id, request, options, cancellationToken);
    }

    public ValueTask Stop()
    {
        return system.Stop(Id);
    }

    public ValueTask<ActorStopResult> Stop(TimeSpan drainTimeout)
    {
        return system.Stop(Id, drainTimeout);
    }

    public MailboxMetrics GetMailboxMetrics()
    {
        return system.GetMailboxMetrics(Id);
    }

    public ActorState GetState()
    {
        return system.GetActorState(Id);
    }
}
