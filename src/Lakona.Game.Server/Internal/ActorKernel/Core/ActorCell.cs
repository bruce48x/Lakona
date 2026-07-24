using Lakona.Game.Server.Internal.ActorKernel.Abstractions;
using Lakona.Game.Server.Internal.ActorKernel.Messaging;
using MailboxCore = Lakona.Game.Server.Internal.ActorKernel.Mailbox.Mailbox;

namespace Lakona.Game.Server.Internal.ActorKernel.Core;

internal sealed class ActorCell
{
    private readonly ActorTurnRunner turnRunner;
    private readonly ActorCellStopSequence stopSequence;

    public ActorCell(
        ActorSystem system,
        ActorRef self,
        IActor actor,
        int mailboxCapacity,
        TimeSpan? slowMessageThreshold)
    {
        turnRunner = new ActorTurnRunner(system, self, actor, slowMessageThreshold);
        Mailbox = new MailboxCore(turnRunner.Dispatch, mailboxCapacity);
        stopSequence = new ActorCellStopSequence(Mailbox);
    }

    internal MailboxCore Mailbox { get; }

    internal Task Completion => Mailbox.Completion;

    internal bool IsStopping => stopSequence.IsStopping;

    internal ActorState State => stopSequence.GetState();

    public MailboxMetrics GetMailboxMetrics()
    {
        return Mailbox.GetMetrics();
    }

    public ValueTask Send(Envelope envelope, CancellationToken cancellationToken)
    {
        return Mailbox.Send(envelope, cancellationToken);
    }

    public bool TrySend(Envelope envelope)
    {
        return Mailbox.TrySend(envelope);
    }

    public ValueTask StopAsync()
    {
        return stopSequence.StopAsync();
    }

    public Task RequestStopAsync() => stopSequence.RequestStopAsync();
}
