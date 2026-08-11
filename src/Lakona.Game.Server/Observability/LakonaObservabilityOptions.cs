using Lakona.Game.Server.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Observability;

public sealed class LakonaObservabilityOptions
{
    public LakonaLocalAdminObservabilityOptions LocalAdmin { get; init; } = new();
    public LakonaDiagnosticsObservabilityOptions Diagnostics { get; init; } = new();
    public LakonaMetricsObservabilityOptions Metrics { get; init; } = new();
    public LakonaTracingObservabilityOptions Tracing { get; init; } = new();

    public static LakonaObservabilityOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Lakona:Observability");

        return new LakonaObservabilityOptions
        {
            LocalAdmin = LakonaLocalAdminObservabilityOptions.FromConfiguration(
                section.GetSection("LocalAdmin")),
            Diagnostics = LakonaDiagnosticsObservabilityOptions.FromConfiguration(
                section.GetSection("Diagnostics")),
            Metrics = LakonaMetricsObservabilityOptions.FromConfiguration(section.GetSection("Metrics")),
            Tracing = LakonaTracingObservabilityOptions.FromConfiguration(section.GetSection("Tracing"))
        };
    }

    public static LakonaObservabilityOptions Defaults()
    {
        return FromConfiguration(new ConfigurationBuilder().Build());
    }
}

public sealed class LakonaLocalAdminObservabilityOptions
{
    public bool? Enabled { get; init; }
    public bool EffectiveEnabled { get; init; }
    public bool RequireLoopback { get; init; } = true;

    public static LakonaLocalAdminObservabilityOptions FromConfiguration(IConfiguration section)
    {
        var enabled = bool.TryParse(section["Enabled"], out var parsed) ? parsed : (bool?)null;

        return new LakonaLocalAdminObservabilityOptions
        {
            Enabled = enabled,
            EffectiveEnabled = enabled ?? false,
            RequireLoopback = LakonaConfigurationReader.ReadBool(section, "RequireLoopback", true)
        };
    }
}

public sealed class LakonaDiagnosticsObservabilityOptions
{
    public bool DetailEnabled { get; init; }
    public LakonaDiagnosticsEventBufferOptions EventBuffer { get; init; } = new();

    public static LakonaDiagnosticsObservabilityOptions FromConfiguration(IConfiguration section)
    {
        return new LakonaDiagnosticsObservabilityOptions
        {
            DetailEnabled = LakonaConfigurationReader.ReadBool(section, "DetailEnabled", false),
            EventBuffer = LakonaDiagnosticsEventBufferOptions.FromConfiguration(
                section.GetSection("EventBuffer"))
        };
    }
}

public sealed class LakonaDiagnosticsEventBufferOptions
{
    public bool Enabled { get; init; } = true;
    public int Capacity { get; init; } = 1024;
    internal string CapacityRaw { get; init; } = "1024";
    public LogLevel MinimumLevel { get; init; } = LogLevel.Warning;

    public static LakonaDiagnosticsEventBufferOptions FromConfiguration(IConfiguration section)
    {
        return new LakonaDiagnosticsEventBufferOptions
        {
            Enabled = LakonaConfigurationReader.ReadBool(section, "Enabled", true),
            Capacity = LakonaConfigurationReader.ReadInt(section, "Capacity", 1024),
            CapacityRaw = section["Capacity"] ?? "1024",
            MinimumLevel = LakonaConfigurationReader.ReadLogLevel(
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
            Enabled = LakonaConfigurationReader.ReadBool(section, "Enabled", false),
            Path = LakonaConfigurationReader.ReadString(section, "Path", "/_lakona/metrics")
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
    internal string SampleRateRaw { get; init; } = "1.0";

    public static LakonaTraceExportObservabilityOptions FromConfiguration(IConfiguration section)
    {
        return new LakonaTraceExportObservabilityOptions
        {
            Enabled = LakonaConfigurationReader.ReadBool(section, "Enabled", false),
            SampleRate = LakonaConfigurationReader.ReadDouble(section, "SampleRate", 1.0),
            SampleRateRaw = section["SampleRate"] ?? "1.0"
        };
    }
}
