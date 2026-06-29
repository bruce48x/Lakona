using Lakona.Game.Server.Guardrails;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Observability;

public sealed class LakonaObservabilityOptions
{
    public LakonaLoggingObservabilityOptions Logging { get; init; } = new();
    public LakonaLocalAdminObservabilityOptions LocalAdmin { get; init; } = new();
    public LakonaDiagnosticsObservabilityOptions Diagnostics { get; init; } = new();
    public LakonaMetricsObservabilityOptions Metrics { get; init; } = new();
    public LakonaTracingObservabilityOptions Tracing { get; init; } = new();

    public static LakonaObservabilityOptions FromConfiguration(
        IConfiguration configuration,
        LakonaGameRuntimeProfile profile)
    {
        var section = configuration.GetSection("Lakona:Observability");

        return new LakonaObservabilityOptions
        {
            Logging = LakonaLoggingObservabilityOptions.FromConfiguration(section.GetSection("Logging")),
            LocalAdmin = LakonaLocalAdminObservabilityOptions.FromConfiguration(
                section.GetSection("LocalAdmin"),
                profile),
            Diagnostics = LakonaDiagnosticsObservabilityOptions.FromConfiguration(
                section.GetSection("Diagnostics")),
            Metrics = LakonaMetricsObservabilityOptions.FromConfiguration(section.GetSection("Metrics")),
            Tracing = LakonaTracingObservabilityOptions.FromConfiguration(section.GetSection("Tracing"))
        };
    }

    public static LakonaObservabilityOptions Defaults(LakonaGameRuntimeProfile profile)
    {
        return FromConfiguration(new ConfigurationBuilder().Build(), profile);
    }

    internal static bool ReadBool(IConfiguration section, string name, bool fallback)
    {
        return bool.TryParse(section[name], out var parsed) ? parsed : fallback;
    }

    internal static int ReadInt(IConfiguration section, string name, int fallback)
    {
        return int.TryParse(section[name], out var parsed) ? parsed : fallback;
    }

    internal static long ReadLong(IConfiguration section, string name, long fallback)
    {
        return long.TryParse(section[name], out var parsed) ? parsed : fallback;
    }

    internal static double ReadDouble(IConfiguration section, string name, double fallback)
    {
        return double.TryParse(section[name], out var parsed) ? parsed : fallback;
    }

    internal static string ReadString(IConfiguration section, string name, string fallback)
    {
        var value = section[name];
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    internal static LogLevel ReadLogLevel(IConfiguration section, string name, LogLevel fallback)
    {
        var value = section[name];
        return Enum.TryParse<LogLevel>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
    }
}

public sealed class LakonaLoggingObservabilityOptions
{
    public bool Enabled { get; init; } = true;
    public LogLevel MinimumLevel { get; init; } = LogLevel.Information;
    public IReadOnlyDictionary<string, string> Categories { get; init; } = CreateDefaultCategoryLevels();

    public LakonaConsoleLoggingObservabilityOptions Console { get; init; } = new();
    public LakonaFileLoggingObservabilityOptions File { get; init; } = new();

    public static LakonaLoggingObservabilityOptions FromConfiguration(IConfiguration section)
    {
        return new LakonaLoggingObservabilityOptions
        {
            Enabled = LakonaObservabilityOptions.ReadBool(section, "Enabled", true),
            MinimumLevel = LakonaObservabilityOptions.ReadLogLevel(
                section,
                "MinimumLevel",
                LogLevel.Information),
            Categories = ReadCategoryLevels(section.GetSection("Categories")),
            Console = LakonaConsoleLoggingObservabilityOptions.FromConfiguration(section.GetSection("Console")),
            File = LakonaFileLoggingObservabilityOptions.FromConfiguration(section.GetSection("File"))
        };
    }

    private static IReadOnlyDictionary<string, string> ReadCategoryLevels(IConfigurationSection section)
    {
        var defaults = CreateDefaultCategoryLevels();

        foreach (var child in section.GetChildren())
        {
            defaults[child.Key] = string.IsNullOrWhiteSpace(child.Value) ? "Information" : child.Value;
        }

        return defaults;
    }

    private static Dictionary<string, string> CreateDefaultCategoryLevels()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Lakona.Rpc"] = "Information",
            ["Lakona.Rpc.Transport"] = "Information",
            ["Lakona.Game.Server"] = "Information",
            ["Lakona.Game.Session"] = "Information",
            ["Lakona.Game.Actor"] = "Information",
            ["Lakona.Game.Cluster"] = "Information",
            ["Lakona.Game.Hotfix"] = "Information",
            ["Lakona.Game.Observability"] = "Information"
        };
    }
}

public sealed class LakonaConsoleLoggingObservabilityOptions
{
    public bool Enabled { get; init; } = true;
    public string Format { get; init; } = "Compact";
    public bool IncludeScopes { get; init; }

    public static LakonaConsoleLoggingObservabilityOptions FromConfiguration(IConfiguration section)
    {
        return new LakonaConsoleLoggingObservabilityOptions
        {
            Enabled = LakonaObservabilityOptions.ReadBool(section, "Enabled", true),
            Format = LakonaObservabilityOptions.ReadString(section, "Format", "Compact"),
            IncludeScopes = LakonaObservabilityOptions.ReadBool(section, "IncludeScopes", false)
        };
    }
}

public sealed class LakonaFileLoggingObservabilityOptions
{
    public bool Enabled { get; init; }
    public string Path { get; init; } = "logs/lakona-.log";
    public string RollingInterval { get; init; } = "Day";
    public int RetainedFileCount { get; init; } = 7;
    public int FileSizeLimitMB { get; init; } = 128;

    public static LakonaFileLoggingObservabilityOptions FromConfiguration(IConfiguration section)
    {
        return new LakonaFileLoggingObservabilityOptions
        {
            Enabled = LakonaObservabilityOptions.ReadBool(section, "Enabled", false),
            Path = LakonaObservabilityOptions.ReadString(section, "Path", "logs/lakona-.log"),
            RollingInterval = LakonaObservabilityOptions.ReadString(section, "RollingInterval", "Day"),
            RetainedFileCount = LakonaObservabilityOptions.ReadInt(
                section,
                "RetainedFileCount",
                7),
            FileSizeLimitMB = LakonaObservabilityOptions.ReadInt(
                section,
                "FileSizeLimitMB",
                128)
        };
    }
}

public sealed class LakonaLocalAdminObservabilityOptions
{
    public bool? Enabled { get; init; }
    public bool EffectiveEnabled { get; init; }
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 20090;
    public bool RequireLoopback { get; init; } = true;

    public static LakonaLocalAdminObservabilityOptions FromConfiguration(
        IConfiguration section,
        LakonaGameRuntimeProfile profile)
    {
        var enabled = bool.TryParse(section["Enabled"], out var parsed) ? parsed : (bool?)null;

        return new LakonaLocalAdminObservabilityOptions
        {
            Enabled = enabled,
            EffectiveEnabled = enabled ?? profile == LakonaGameRuntimeProfile.Development,
            Host = LakonaObservabilityOptions.ReadString(section, "Host", "127.0.0.1"),
            Port = LakonaObservabilityOptions.ReadInt(section, "Port", 20090),
            RequireLoopback = LakonaObservabilityOptions.ReadBool(section, "RequireLoopback", true)
        };
    }
}

public sealed class LakonaDiagnosticsObservabilityOptions
{
    public bool SummaryEnabled { get; init; } = true;
    public bool DetailEnabled { get; init; }
    public LakonaDiagnosticsEventBufferOptions EventBuffer { get; init; } = new();

    public static LakonaDiagnosticsObservabilityOptions FromConfiguration(IConfiguration section)
    {
        return new LakonaDiagnosticsObservabilityOptions
        {
            SummaryEnabled = LakonaObservabilityOptions.ReadBool(section, "SummaryEnabled", true),
            DetailEnabled = LakonaObservabilityOptions.ReadBool(section, "DetailEnabled", false),
            EventBuffer = LakonaDiagnosticsEventBufferOptions.FromConfiguration(
                section.GetSection("EventBuffer"))
        };
    }
}

public sealed class LakonaDiagnosticsEventBufferOptions
{
    public bool Enabled { get; init; } = true;
    public int Capacity { get; init; } = 1024;
    public LogLevel MinimumLevel { get; init; } = LogLevel.Warning;

    public static LakonaDiagnosticsEventBufferOptions FromConfiguration(IConfiguration section)
    {
        return new LakonaDiagnosticsEventBufferOptions
        {
            Enabled = LakonaObservabilityOptions.ReadBool(section, "Enabled", true),
            Capacity = LakonaObservabilityOptions.ReadInt(section, "Capacity", 1024),
            MinimumLevel = LakonaObservabilityOptions.ReadLogLevel(
                section,
                "MinimumLevel",
                LogLevel.Warning)
        };
    }
}

public sealed class LakonaMetricsObservabilityOptions
{
    public LakonaPrometheusObservabilityOptions Prometheus { get; init; } = new();

    public static LakonaMetricsObservabilityOptions FromConfiguration(IConfiguration section)
    {
        return new LakonaMetricsObservabilityOptions
        {
            Prometheus = LakonaPrometheusObservabilityOptions.FromConfiguration(
                section.GetSection("Prometheus"))
        };
    }
}

public sealed class LakonaPrometheusObservabilityOptions
{
    public bool Enabled { get; init; }
    public string Path { get; init; } = "/_lakona/metrics";

    public static LakonaPrometheusObservabilityOptions FromConfiguration(IConfiguration section)
    {
        return new LakonaPrometheusObservabilityOptions
        {
            Enabled = LakonaObservabilityOptions.ReadBool(section, "Enabled", false),
            Path = LakonaObservabilityOptions.ReadString(section, "Path", "/_lakona/metrics")
        };
    }
}

public sealed class LakonaTracingObservabilityOptions
{
    public LakonaTraceExportObservabilityOptions Export { get; init; } = new();

    public static LakonaTracingObservabilityOptions FromConfiguration(IConfiguration section)
    {
        return new LakonaTracingObservabilityOptions
        {
            Export = LakonaTraceExportObservabilityOptions.FromConfiguration(section.GetSection("Export"))
        };
    }
}

public sealed class LakonaTraceExportObservabilityOptions
{
    public bool Enabled { get; init; }
    public double SampleRate { get; init; } = 1.0;

    public static LakonaTraceExportObservabilityOptions FromConfiguration(IConfiguration section)
    {
        return new LakonaTraceExportObservabilityOptions
        {
            Enabled = LakonaObservabilityOptions.ReadBool(section, "Enabled", false),
            SampleRate = LakonaObservabilityOptions.ReadDouble(section, "SampleRate", 1.0)
        };
    }
}
