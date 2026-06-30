namespace Lakona.Game.Server.Hotfix.Abstractions.Timers;

public readonly struct TimerTick<TArgs>
{
    public TimerTick(
        TimerId timerId,
        TArgs args,
        IServiceProvider services,
        DateTimeOffset dueAtUtc,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        TimerId = timerId;
        Args = args;
        Services = services;
        DueAtUtc = dueAtUtc;
        ObservedAtUtc = observedAtUtc;
        CancellationToken = cancellationToken;
    }

    public TimerId TimerId { get; }

    public TArgs Args { get; }

    public IServiceProvider Services { get; }

    public DateTimeOffset DueAtUtc { get; }

    public DateTimeOffset ObservedAtUtc { get; }

    public CancellationToken CancellationToken { get; }
}
