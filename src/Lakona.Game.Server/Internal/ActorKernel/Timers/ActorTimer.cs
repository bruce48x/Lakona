using System.Diagnostics;
using Lakona.Game.Server.Internal.ActorKernel.Messaging;

namespace Lakona.Game.Server.Internal.ActorKernel.Timers;

internal sealed class ActorTimer : IDisposable
{
    private readonly ActorRef target;
    private readonly object message;
    private readonly ActivityContext parentActivityContext;
    private readonly TimeSpan dueTime;
    private readonly TimeSpan period;
    private readonly Timer timer;
    private readonly object gate = new();
    private bool disposed;

    internal ActorTimer(
        ActorRef target,
        object message,
        TimeSpan dueTime,
        TimeSpan period,
        ActivityContext parentActivityContext)
    {
        this.target = target;
        this.message = message;
        this.parentActivityContext = parentActivityContext;
        this.dueTime = dueTime;
        this.period = period;
        timer = new Timer(OnTick);
    }

    internal void Start()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            timer.Change(dueTime, period);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            timer.Dispose();
        }
    }

    private void OnTick(object? state)
    {
        try
        {
            target.TrySend(message, parentActivityContext);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}
