namespace Lakona.Game.Server.Hotfix.Abstractions.Timers;

/// <summary>
/// Provides callback data for one timer tick.
/// </summary>
/// <typeparam name="TArgs">The timer argument type supplied when the timer was created.</typeparam>
public sealed class TimerTick<TArgs>
{
    /// <summary>
    /// Initializes timer tick data.
    /// </summary>
    /// <param name="timerId">The timer id that produced this tick.</param>
    /// <param name="args">The timer arguments.</param>
    /// <param name="services">The current hotfix service provider.</param>
    /// <param name="dueAtUtc">The scheduled UTC due time for this tick.</param>
    /// <param name="observedAtUtc">The UTC time at which the scheduler observed the due tick.</param>
    /// <param name="cancellationToken">The callback cancellation token.</param>
    public TimerTick(
        TimerId timerId,
        TArgs args,
        IServiceProvider services,
        DateTimeOffset dueAtUtc,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        TimerId = timerId;
        Args = args;
        Services = services;
        DueAtUtc = dueAtUtc;
        ObservedAtUtc = observedAtUtc;
        CancellationToken = cancellationToken;
    }

    /// <summary>
    /// Gets the timer id that produced this tick.
    /// </summary>
    public TimerId TimerId { get; }

    /// <summary>
    /// Gets the arguments supplied when the timer was created.
    /// </summary>
    public TArgs Args { get; }

    /// <summary>
    /// Gets the current hotfix service provider.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Gets the scheduled UTC due time for this tick.
    /// </summary>
    public DateTimeOffset DueAtUtc { get; }

    /// <summary>
    /// Gets the UTC time at which the scheduler observed this tick as due.
    /// </summary>
    public DateTimeOffset ObservedAtUtc { get; }

    /// <summary>
    /// Gets the cancellation token for the callback invocation.
    /// </summary>
    public CancellationToken CancellationToken { get; }
}
