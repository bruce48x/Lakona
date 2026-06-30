using Lakona.Game.Server.Hotfix.Abstractions.Timers;

namespace Lakona.Game.Server.Hotfix.Timers;

internal sealed class LakonaTimerRegistration
{
    private CancellationTokenSource? dispatchCancellation;

    public LakonaTimerRegistration(LakonaTimerDescriptor descriptor)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        NextDueAtUtc = descriptor.NextDueAtUtc;
        Period = descriptor.Period;
    }

    public LakonaTimerDescriptor Descriptor { get; }

    public TimerId TimerId => Descriptor.TimerId;

    public DateTimeOffset NextDueAtUtc { get; set; }

    public long NextDueTimestamp { get; set; }

    public TimeSpan? Period { get; }

    public long Generation { get; set; }

    public long DispatchGeneration { get; set; }

    public bool Pending { get; set; }

    public bool FollowUpScheduled { get; set; }

    public bool Destroyed { get; private set; }

    public CancellationTokenSource? DispatchCancellation
    {
        get => dispatchCancellation;
        set
        {
            dispatchCancellation?.Dispose();
            dispatchCancellation = value;
        }
    }

    public void Destroy()
    {
        Destroyed = true;
    }

    public CancellationTokenSource? TakeDispatchCancellation()
    {
        var cancellation = dispatchCancellation;
        dispatchCancellation = null;
        return cancellation;
    }
}

internal readonly record struct LakonaTimerHeapEntry(TimerId TimerId, long Generation);
