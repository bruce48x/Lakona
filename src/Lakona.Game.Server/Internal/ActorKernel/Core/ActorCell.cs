using Lakona.Game.Server.Internal.ActorKernel.Abstractions;
using Lakona.Game.Server.Internal.ActorKernel.Lifecycle;
using Lakona.Game.Server.Internal.ActorKernel.Messaging;
using MailboxCore = Lakona.Game.Server.Internal.ActorKernel.Mailbox.Mailbox;

namespace Lakona.Game.Server.Internal.ActorKernel.Core;

internal sealed class ActorCell
{
    private readonly IActor actor;
    private readonly ActorTimerSet timers = new();
    private readonly ActorTurnRunner turnRunner;
    private readonly ActorCellStopSequence stopSequence;

    public ActorCell(
        ActorSystem system,
        ActorRef self,
        IActor actor,
        Type messageType,
        int mailboxCapacity,
        TimeSpan? slowMessageThreshold,
        string? name)
    {
        Self = self;
        this.actor = actor;
        Name = name;
        MessageType = messageType;
        turnRunner = new ActorTurnRunner(system, this, self, actor, slowMessageThreshold);
        Mailbox = new MailboxCore(turnRunner.Dispatch, mailboxCapacity);
        stopSequence = new ActorCellStopSequence(Mailbox, timers);
    }

    internal ActorRef Self { get; }

    internal Type MessageType { get; }

    internal string? Name { get; }

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

    public async ValueTask StartAsync()
    {
        ActorContextCore context = new(Self, this, new Envelope(ActorLifecycleMessage.Started));
        await actor.OnStarted(context).ConfigureAwait(false);
    }

    internal void AddTimer(IDisposable timer)
    {
        timers.Add(timer);
    }

    public void Complete()
    {
        Mailbox.Complete();
    }

    public async ValueTask StopAsync()
    {
        await stopSequence.StopAsync().ConfigureAwait(false);
    }

    public async ValueTask<ActorStopResult> StopAsync(TimeSpan drainTimeout)
    {
        return await stopSequence.StopAsync(drainTimeout).ConfigureAwait(false);
    }

    public Task RequestStopAsync(bool runStoppingHook = true)
    {
        return stopSequence.RequestStopAsync(runStoppingHook);
    }
}
