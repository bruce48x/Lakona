using Microsoft.Extensions.Configuration;
using Lakona.Game.Server.Configuration;

namespace Lakona.Game.Server.Sessions;

public sealed class LakonaNotificationOptions
{
    public int BatchWindowMilliseconds { get; init; } = 10;

    public int MaximumBatchSize { get; init; } = 256;

    public int MaximumBatchBytes { get; init; } = 256 * 1024;

    public int MaximumPendingPerSession { get; init; } = 256;

    public int MaximumPendingPerProcess { get; init; } = 65_536;

    internal static LakonaNotificationOptions FromConfiguration(IConfiguration section)
    {
        var defaults = new LakonaNotificationOptions();
        return new LakonaNotificationOptions
        {
            BatchWindowMilliseconds = LakonaConfigurationReader.ReadInt(
                section, nameof(BatchWindowMilliseconds), defaults.BatchWindowMilliseconds),
            MaximumBatchSize = LakonaConfigurationReader.ReadInt(
                section, nameof(MaximumBatchSize), defaults.MaximumBatchSize),
            MaximumBatchBytes = LakonaConfigurationReader.ReadInt(
                section, nameof(MaximumBatchBytes), defaults.MaximumBatchBytes),
            MaximumPendingPerSession = LakonaConfigurationReader.ReadInt(
                section, nameof(MaximumPendingPerSession), defaults.MaximumPendingPerSession),
            MaximumPendingPerProcess = LakonaConfigurationReader.ReadInt(
                section, nameof(MaximumPendingPerProcess), defaults.MaximumPendingPerProcess)
        };
    }

    internal ClientNotificationBatchOptions ToBatchOptions() => new()
    {
        Window = TimeSpan.FromMilliseconds(BatchWindowMilliseconds),
        MaximumBatchSize = MaximumBatchSize,
        MaximumBatchBytes = MaximumBatchBytes
    };
}
