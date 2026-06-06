using System.Runtime.ExceptionServices;
using Lakona.Actor.Lifecycle;
using Lakona.Actor.Messaging;
using MailboxCore = Lakona.Actor.Mailbox.Mailbox;

namespace Lakona.Actor.Core;

internal sealed class ActorCellStopSequence
{
    private readonly MailboxCore mailbox;
    private readonly ActorTimerSet timers;
    private readonly object stopGate = new();
    private Task? stopTask;
    private int stopping;

    internal ActorCellStopSequence(MailboxCore mailbox, ActorTimerSet timers)
    {
        ArgumentNullException.ThrowIfNull(mailbox);
        ArgumentNullException.ThrowIfNull(timers);

        this.mailbox = mailbox;
        this.timers = timers;
    }

    internal bool IsStopping => Volatile.Read(ref stopping) != 0;

    internal ActorState GetState()
    {
        return Volatile.Read(ref stopping) == 0 ? ActorState.Active
            : mailbox.Completion.IsCompleted ? ActorState.Dead
            : ActorState.Draining;
    }

    internal async ValueTask StopAsync()
    {
        await RequestStopAsync(runStoppingHook: false).ConfigureAwait(false);
    }

    internal async ValueTask<ActorStopResult> StopAsync(TimeSpan drainTimeout)
    {
        if (drainTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(drainTimeout), "Drain timeout must be greater than zero.");
        }

        return await WaitForStop(RequestStopAsync(runStoppingHook: false), drainTimeout).ConfigureAwait(false);
    }

    internal Task RequestStopAsync(bool runStoppingHook = true)
    {
        lock (stopGate)
        {
            if (stopTask is not null)
            {
                return stopTask;
            }

            Interlocked.Exchange(ref stopping, 1);
            timers.DisposeAll();
            stopTask = RunStopSequenceAsync(runStoppingHook);
            return stopTask;
        }
    }

    private async Task RunStopSequenceAsync(bool runStoppingHook)
    {
        Exception? stopError = null;

        try
        {
            if (runStoppingHook)
            {
                TaskCompletionSource<object?> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
                await mailbox.Send(new Envelope(ActorLifecycleMessage.Stopping, stopped), CancellationToken.None).ConfigureAwait(false);
                await stopped.Task.ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            stopError = ex;
        }
        finally
        {
            mailbox.Complete();
            await mailbox.Completion.ConfigureAwait(false);
        }

        if (stopError is not null)
        {
            ExceptionDispatchInfo.Capture(stopError).Throw();
        }
    }

    private static async ValueTask<ActorStopResult> WaitForStop(Task stopTask, TimeSpan drainTimeout)
    {
        try
        {
            await stopTask.WaitAsync(drainTimeout).ConfigureAwait(false);
            return ActorStopResult.Drained;
        }
        catch (TimeoutException)
        {
            return ActorStopResult.TimedOut;
        }
    }
}
