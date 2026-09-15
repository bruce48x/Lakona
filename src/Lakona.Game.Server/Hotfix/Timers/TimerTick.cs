namespace Lakona.Game.Server.Hotfix.Timers;

/// <summary>Provides the timer identity and arguments for one callback.</summary>
/// <typeparam name="TArgs">The argument type supplied when the timer was created.</typeparam>
public sealed class TimerTick<TArgs>
{
    /// <summary>Initializes callback data.</summary>
    public TimerTick(TimerId timerId, TArgs args)
    {
        TimerId = timerId;
        Args = args;
    }

    /// <summary>Gets the timer that produced this callback.</summary>
    public TimerId TimerId { get; }

    /// <summary>Gets the arguments supplied when the timer was created.</summary>
    public TArgs Args { get; }
}
