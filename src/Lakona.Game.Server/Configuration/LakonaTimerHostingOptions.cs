using Microsoft.Extensions.Configuration;

namespace Lakona.Game.Server.Configuration;

/// <summary>
/// Configures process-owned Hotfix timer capacity.
/// </summary>
public sealed class LakonaTimerHostingOptions
{
    internal const int DefaultMaxActiveTimers = 65_536;

    /// <summary>
    /// Gets the maximum number of active timers owned by one server process.
    /// </summary>
    public int MaxActiveTimers { get; init; } = DefaultMaxActiveTimers;

    internal static LakonaTimerHostingOptions FromConfiguration(IConfigurationSection section)
    {
        var defaults = new LakonaTimerHostingOptions();
        var maxActiveTimers = LakonaConfigurationReader.ReadInt(
            section,
            nameof(MaxActiveTimers),
            defaults.MaxActiveTimers);
        if (maxActiveTimers <= 0)
        {
            throw new InvalidOperationException(
                $"{section.Path}:{nameof(MaxActiveTimers)} must be greater than zero.");
        }

        return new LakonaTimerHostingOptions { MaxActiveTimers = maxActiveTimers };
    }
}
