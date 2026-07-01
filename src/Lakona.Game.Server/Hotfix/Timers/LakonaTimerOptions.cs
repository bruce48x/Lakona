namespace Lakona.Game.Server.Hotfix.Timers;

internal sealed class LakonaTimerOptions
{
    public int MaxConcurrentCallbacks { get; set; } = Math.Max(1, Environment.ProcessorCount);

    public int DispatchQueueCapacity { get; set; } = 1024;

    public void Validate()
    {
        if (MaxConcurrentCallbacks <= 0)
        {
            throw new InvalidOperationException("Lakona timer max concurrent callbacks must be greater than zero.");
        }

        if (DispatchQueueCapacity <= 0)
        {
            throw new InvalidOperationException("Lakona timer dispatch queue capacity must be greater than zero.");
        }
    }
}
