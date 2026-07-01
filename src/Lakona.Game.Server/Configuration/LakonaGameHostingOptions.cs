using Microsoft.Extensions.Configuration;

namespace Lakona.Game.Server.Configuration;

public sealed class LakonaGameHostingOptions
{
    public LakonaActorHostingOptions Actors { get; init; } = new();

    public LakonaSessionHostingOptions Sessions { get; init; } = new();

    public static LakonaGameHostingOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = GetRuntimeSection(configuration);
        return new LakonaGameHostingOptions
        {
            Actors = LakonaActorHostingOptions.FromConfiguration(section.GetSection("Actors")),
            Sessions = LakonaSessionHostingOptions.FromConfiguration(section.GetSection("Sessions"))
        };
    }

    private static IConfigurationSection GetRuntimeSection(IConfiguration configuration)
    {
        return configuration.GetSection("Lakona");
    }
}

public sealed class LakonaActorHostingOptions
{
    public int MailboxCapacity { get; init; } = 4096;

    public TimeSpan CallTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan? SlowMessageThreshold { get; init; }

    public static LakonaActorHostingOptions FromConfiguration(IConfiguration section)
    {
        var defaults = new LakonaActorHostingOptions();
        return new LakonaActorHostingOptions
        {
            MailboxCapacity = LakonaConfigurationReader.ReadInt(section, "MailboxCapacity", defaults.MailboxCapacity),
            CallTimeout = LakonaConfigurationReader.ReadSeconds(section, "CallTimeoutSeconds", defaults.CallTimeout),
            SlowMessageThreshold = LakonaConfigurationReader.ReadNullableSeconds(
                section,
                "SlowMessageThresholdSeconds",
                defaults.SlowMessageThreshold)
        };
    }

    internal void ApplyTo(Actors.ActorRuntimeOptions options)
    {
        options.MailboxCapacity = MailboxCapacity;
        options.CallTimeout = CallTimeout;
        options.SlowMessageThreshold = SlowMessageThreshold;
    }
}

public sealed class LakonaSessionHostingOptions
{
    public LakonaSessionCleanupHostingOptions Cleanup { get; init; } = new();

    public static LakonaSessionHostingOptions FromConfiguration(IConfiguration section)
    {
        return new LakonaSessionHostingOptions
        {
            Cleanup = LakonaSessionCleanupHostingOptions.FromConfiguration(section.GetSection("Cleanup"))
        };
    }
}

public sealed class LakonaSessionCleanupHostingOptions
{
    public bool Enabled { get; init; } = true;

    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan DisconnectedSessionRetention { get; init; } = TimeSpan.FromMinutes(2);

    public static LakonaSessionCleanupHostingOptions FromConfiguration(IConfiguration section)
    {
        var defaults = new LakonaSessionCleanupHostingOptions();
        return new LakonaSessionCleanupHostingOptions
        {
            Enabled = LakonaConfigurationReader.ReadBool(section, "Enabled", defaults.Enabled),
            Interval = LakonaConfigurationReader.ReadSeconds(section, "IntervalSeconds", defaults.Interval),
            DisconnectedSessionRetention = LakonaConfigurationReader.ReadSeconds(
                section,
                "DisconnectedRetentionSeconds",
                defaults.DisconnectedSessionRetention)
        };
    }

    internal void ApplyTo(Sessions.SessionCleanupOptions options)
    {
        options.Interval = Interval;
        options.DisconnectedSessionRetention = DisconnectedSessionRetention;
    }
}
