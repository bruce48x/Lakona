using MailboxCore = Lakona.Game.Server.Internal.ActorKernel.Mailbox.Mailbox;

namespace Lakona.Game.Server.Internal.ActorKernel.Core;

internal sealed class ActorCellStopSequence
{
    private readonly MailboxCore mailbox;
    private readonly object stopGate = new();
    private Task? stopTask;
    private int stopping;

    internal ActorCellStopSequence(MailboxCore mailbox)
    {
        ArgumentNullException.ThrowIfNull(mailbox);

        this.mailbox = mailbox;
    }

    internal bool IsStopping => Volatile.Read(ref stopping) != 0;

    internal ActorState GetState()
    {
        return Volatile.Read(ref stopping) == 0 ? ActorState.Active
            : mailbox.Completion.IsCompleted ? ActorState.Dead
            : ActorState.Draining;
    }

    internal ValueTask StopAsync() => new(RequestStopAsync());

    internal Task RequestStopAsync()
    {
        lock (stopGate)
        {
            if (stopTask is not null)
            {
                return stopTask;
            }

            Interlocked.Exchange(ref stopping, 1);
            mailbox.Complete();
            stopTask = mailbox.Completion;
            return stopTask;
        }
    }
}
