using Lakona.Game.Server.Configuration;

namespace Lakona.Game.Server.Hotfix.Timers;

internal sealed class LakonaTimerOptions
{
    public int DispatchQueueCapacity { get; set; } = 1024;

    public int MaxActiveTimers { get; set; } = LakonaTimerHostingOptions.DefaultMaxActiveTimers;

    public void Validate()
    {
        if (DispatchQueueCapacity <= 0)
        {
            throw new InvalidOperationException("Lakona timer dispatch queue capacity must be greater than zero.");
        }

        if (MaxActiveTimers <= 0)
        {
            throw new InvalidOperationException("Lakona timer maximum active timers must be greater than zero.");
        }
    }
}
