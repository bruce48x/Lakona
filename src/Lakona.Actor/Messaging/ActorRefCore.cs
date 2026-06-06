using System.Diagnostics;

namespace Lakona.Actor.Messaging;

internal sealed class ActorRef
{
    private readonly ActorSystem system;

    internal ActorRef(ActorSystem system, ActorId id)
    {
        this.system = system;
        Id = id;
    }

    public ActorId Id { get; }

    public ValueTask Send(object message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return system.Send(Id, message, cancellationToken);
    }

    internal ValueTask Send(
        object message,
        ActivityContext parentActivityContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return system.Send(Id, message, parentActivityContext, cancellationToken);
    }

    public ActorSendResult TrySend(object message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return system.TrySend(Id, message);
    }

    internal ActorSendResult TrySend(object message, ActivityContext parentActivityContext)
    {
        ArgumentNullException.ThrowIfNull(message);

        return system.TrySend(Id, message, parentActivityContext);
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

    public override string ToString() => Id.ToString();
}
