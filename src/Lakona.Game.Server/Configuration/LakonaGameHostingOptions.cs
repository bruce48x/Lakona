using Microsoft.Extensions.Configuration;

namespace Lakona.Game.Server.Configuration;

/// <summary>
/// Configures in-process game-server hosting services.
/// </summary>
/// <remarks>
/// These options tune framework service behavior, such as actor mailbox limits
/// and session cleanup. They are distinct from <see cref="LakonaGameRuntimeOptions"/>,
/// which describes deployment topology.
/// </remarks>
public sealed class LakonaGameHostingOptions
{
    /// <summary>
    /// Gets actor runtime hosting options.
    /// </summary>
    public LakonaActorHostingOptions Actors { get; init; } = new();

    /// <summary>
    /// Gets game session hosting options.
    /// </summary>
    public LakonaSessionHostingOptions Sessions { get; init; } = new();

    /// <summary>
    /// Binds hosting options from the <c>Lakona</c> configuration root.
    /// </summary>
    /// <param name="configuration">The host configuration to read.</param>
    /// <returns>The bound hosting options.</returns>
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

/// <summary>
/// Configures the local actor runtime used by hosted game actors.
/// </summary>
public sealed class LakonaActorHostingOptions
{
    /// <summary>
    /// Gets the bounded mailbox capacity for each hosted actor.
    /// </summary>
    public int MailboxCapacity { get; init; } = 4096;

    /// <summary>
    /// Gets the default timeout for actor request/reply calls.
    /// </summary>
    public TimeSpan CallTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the threshold after which actor message handling is reported as slow.
    /// </summary>
    public TimeSpan? SlowMessageThreshold { get; init; }

    /// <summary>
    /// Binds actor hosting options from a <c>Lakona:Actors</c> configuration section.
    /// </summary>
    /// <param name="section">The actor hosting configuration section.</param>
    /// <returns>The bound actor hosting options.</returns>
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

/// <summary>
/// Configures game session framework services.
/// </summary>
public sealed class LakonaSessionHostingOptions
{
    /// <summary>
    /// Gets disconnected-session cleanup options.
    /// </summary>
    public LakonaSessionCleanupHostingOptions Cleanup { get; init; } = new();

    /// <summary>
    /// Binds session hosting options from a <c>Lakona:Sessions</c> configuration section.
    /// </summary>
    /// <param name="section">The session hosting configuration section.</param>
    /// <returns>The bound session hosting options.</returns>
    public static LakonaSessionHostingOptions FromConfiguration(IConfiguration section)
    {
        return new LakonaSessionHostingOptions
        {
            Cleanup = LakonaSessionCleanupHostingOptions.FromConfiguration(section.GetSection("Cleanup"))
        };
    }
}

/// <summary>
/// Configures background cleanup for disconnected game sessions.
/// </summary>
public sealed class LakonaSessionCleanupHostingOptions
{
    /// <summary>
    /// Gets a value indicating whether disconnected-session cleanup is enabled.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets how often the cleanup service scans for expired disconnected sessions.
    /// </summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets how long a disconnected session may remain resumable before cleanup.
    /// </summary>
    public TimeSpan DisconnectedSessionRetention { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Binds cleanup options from a <c>Lakona:Sessions:Cleanup</c> configuration section.
    /// </summary>
    /// <param name="section">The session cleanup configuration section.</param>
    /// <returns>The bound session cleanup options.</returns>
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
