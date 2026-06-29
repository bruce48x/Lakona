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
    public IReadOnlyDictionary<string, string> CategoryLevels { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Lakona"] = "Information",
            ["Lakona.Game"] = "Information",
            ["Lakona.Rpc"] = "Information"
        };

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
            CategoryLevels = ReadCategoryLevels(section.GetSection("CategoryLevels")),
            Console = LakonaConsoleLoggingObservabilityOptions.FromConfiguration(section.GetSection("Console")),
            File = LakonaFileLoggingObservabilityOptions.FromConfiguration(section.GetSection("File"))
        };
    }

    private static IReadOnlyDictionary<string, string> ReadCategoryLevels(IConfigurationSection section)
    {
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Lakona"] = "Information",
            ["Lakona.Game"] = "Information",
            ["Lakona.Rpc"] = "Information"
        };

        foreach (var child in section.GetChildren())
        {
            defaults[child.Key] = string.IsNullOrWhiteSpace(child.Value) ? "Information" : child.Value;
        }

        return defaults;
    }
}

public sealed class LakonaConsoleLoggingObservabilityOptions
{
    public bool Enabled { get; init; } = true;
    public string Format { get; init; } = "compact";
    public bool IncludeScopes { get; init; }

    public static LakonaConsoleLoggingObservabilityOptions FromConfiguration(IConfiguration section)
    {
        return new LakonaConsoleLoggingObservabilityOptions
        {
            Enabled = LakonaObservabilityOptions.ReadBool(section, "Enabled", true),
            Format = LakonaObservabilityOptions.ReadString(section, "Format", "compact"),
            IncludeScopes = LakonaObservabilityOptions.ReadBool(section, "IncludeScopes", false)
        };
    }
}

public sealed class LakonaFileLoggingObservabilityOptions
{
    public bool Enabled { get; init; }
    public string Path { get; init; } = "logs/lakona-.log";
    public string RollingInterval { get; init; } = "Day";
    public int RetainedFileCountLimit { get; init; } = 7;
    public long FileSizeLimitBytes { get; init; } = 128L * 1024L * 1024L;

    public static LakonaFileLoggingObservabilityOptions FromConfiguration(IConfiguration section)
    {
        return new LakonaFileLoggingObservabilityOptions
        {
            Enabled = LakonaObservabilityOptions.ReadBool(section, "Enabled", false),
            Path = LakonaObservabilityOptions.ReadString(section, "Path", "logs/lakona-.log"),
            RollingInterval = LakonaObservabilityOptions.ReadString(section, "RollingInterval", "Day"),
            RetainedFileCountLimit = LakonaObservabilityOptions.ReadInt(
                section,
                "RetainedFileCountLimit",
                7),
            FileSizeLimitBytes = LakonaObservabilityOptions.ReadLong(
                section,
                "FileSizeLimitBytes",
                128L * 1024L * 1024L)
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
    public LakonaDiagnosticsSummaryOptions Summary { get; init; } = new();
    public LakonaDiagnosticsDetailOptions Detail { get; init; } = new();
    public LakonaDiagnosticsEventBufferOptions EventBuffer { get; init; } = new();

    public static LakonaDiagnosticsObservabilityOptions FromConfiguration(IConfiguration section)
    {
        return new LakonaDiagnosticsObservabilityOptions
        {
            Summary = LakonaDiagnosticsSummaryOptions.FromConfiguration(section.GetSection("Summary")),
            Detail = LakonaDiagnosticsDetailOptions.FromConfiguration(section.GetSection("Detail")),
            EventBuffer = LakonaDiagnosticsEventBufferOptions.FromConfiguration(
                section.GetSection("EventBuffer"))
        };
    }
}

public sealed class LakonaDiagnosticsSummaryOptions
{
    public bool Enabled { get; init; } = true;

    public static LakonaDiagnosticsSummaryOptions FromConfiguration(IConfiguration section)
    {
        return new LakonaDiagnosticsSummaryOptions
        {
            Enabled = LakonaObservabilityOptions.ReadBool(section, "Enabled", true)
        };
    }
}

public sealed class LakonaDiagnosticsDetailOptions
{
    public bool Enabled { get; init; }

    public static LakonaDiagnosticsDetailOptions FromConfiguration(IConfiguration section)
    {
        return new LakonaDiagnosticsDetailOptions
        {
            Enabled = LakonaObservabilityOptions.ReadBool(section, "Enabled", false)
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
    public string Endpoint { get; init; } = "";
    public string Protocol { get; init; } = "";
    public double SampleRate { get; init; } = 1.0;

    public static LakonaTraceExportObservabilityOptions FromConfiguration(IConfiguration section)
    {
        return new LakonaTraceExportObservabilityOptions
        {
            Enabled = LakonaObservabilityOptions.ReadBool(section, "Enabled", false),
            Endpoint = LakonaObservabilityOptions.ReadString(section, "Endpoint", ""),
            Protocol = LakonaObservabilityOptions.ReadString(section, "Protocol", ""),
            SampleRate = LakonaObservabilityOptions.ReadDouble(section, "SampleRate", 1.0)
        };
    }
}
