using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using Xunit;

namespace Lakona.Game.Server.Tests.Observability;

public sealed class LakonaObservabilityOptionsTests
{
    [Fact]
    public void LocalAdmin_default_effective_enablement_is_disabled_when_unconfigured()
    {
        var options = LakonaObservabilityOptions.FromConfiguration(BuildConfiguration([]));

        Assert.Null(options.LocalAdmin.Enabled);
        Assert.False(options.LocalAdmin.EffectiveEnabled);
    }

    [Fact]
    public void FromConfiguration_uses_operational_defaults()
    {
        var options = LakonaObservabilityOptions.FromConfiguration(BuildConfiguration([]));

        Assert.True(options.Logging.Enabled);
        Assert.Equal(LogLevel.Information, options.Logging.MinimumLevel);
        Assert.Equal("Information", options.Logging.MinimumLevelRaw);
        Assert.True(options.Logging.Console.Enabled);
        Assert.Equal("Compact", options.Logging.Console.Format);
        Assert.False(options.Logging.Console.IncludeScopes);
        Assert.False(options.Logging.File.Enabled);
        Assert.Equal("logs/lakona-.log", options.Logging.File.Path);
        Assert.Equal("Day", options.Logging.File.RollingInterval);
        Assert.Equal(7, options.Logging.File.RetainedFileCount);
        Assert.Equal(128, options.Logging.File.FileSizeLimitMB);
        Assert.Equal("Information", options.Logging.Categories["Lakona.Rpc"]);
        Assert.Equal("Information", options.Logging.Categories["Lakona.Rpc.Transport"]);
        Assert.Equal("Information", options.Logging.Categories["Lakona.Game.Server"]);
        Assert.Equal("Information", options.Logging.Categories["Lakona.Game.Session"]);
        Assert.Equal("Information", options.Logging.Categories["Lakona.Game.Actor"]);
        Assert.Equal("Information", options.Logging.Categories["Lakona.Game.Cluster"]);
        Assert.Equal("Information", options.Logging.Categories["Lakona.Game.Hotfix"]);
        Assert.Equal("Information", options.Logging.Categories["Lakona.Game.Observability"]);

        Assert.Equal("127.0.0.1", options.LocalAdmin.Host);
        Assert.Equal(20090, options.LocalAdmin.Port);
        Assert.True(options.LocalAdmin.RequireLoopback);
        Assert.False(options.LocalAdmin.EffectiveEnabled);

        Assert.False(options.Diagnostics.DetailEnabled);
        Assert.True(options.Diagnostics.EventBuffer.Enabled);
        Assert.Equal(1024, options.Diagnostics.EventBuffer.Capacity);
        Assert.Equal(LogLevel.Warning, options.Diagnostics.EventBuffer.MinimumLevel);

        Assert.False(options.Metrics.Prometheus.Enabled);
        Assert.Equal("/_lakona/metrics", options.Metrics.Prometheus.Path);

        Assert.False(options.Tracing.Export.Enabled);
        Assert.Equal(1.0, options.Tracing.Export.SampleRate);
    }

    [Fact]
    public void Diagnostics_options_public_surface_does_not_expose_summary_compatibility_switch()
    {
        var propertyNames = typeof(LakonaDiagnosticsObservabilityOptions)
            .GetProperties()
            .Select(static property => property.Name)
            .ToArray();

        Assert.DoesNotContain("SummaryEnabled", propertyNames);
    }

    [Fact]
    public void FromConfiguration_binds_observability_values()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Observability:Logging:Enabled"] = "false",
            ["Lakona:Observability:Logging:MinimumLevel"] = "Debug",
            ["Lakona:Observability:Logging:Categories:Lakona.Game.Server"] = "Trace",
            ["Lakona:Observability:Logging:Console:Enabled"] = "false",
            ["Lakona:Observability:Logging:Console:Format"] = "json",
            ["Lakona:Observability:Logging:Console:IncludeScopes"] = "true",
            ["Lakona:Observability:Logging:File:Enabled"] = "true",
            ["Lakona:Observability:Logging:File:Path"] = "logs/custom-.log",
            ["Lakona:Observability:Logging:File:RollingInterval"] = "Hour",
            ["Lakona:Observability:Logging:File:RetainedFileCount"] = "3",
            ["Lakona:Observability:Logging:File:FileSizeLimitMB"] = "64",
            ["Lakona:Observability:LocalAdmin:Enabled"] = "true",
            ["Lakona:Observability:LocalAdmin:Host"] = "0.0.0.0",
            ["Lakona:Observability:LocalAdmin:Port"] = "20100",
            ["Lakona:Observability:LocalAdmin:RequireLoopback"] = "false",
            ["Lakona:Observability:Diagnostics:DetailEnabled"] = "true",
            ["Lakona:Observability:Diagnostics:EventBuffer:Enabled"] = "false",
            ["Lakona:Observability:Diagnostics:EventBuffer:Capacity"] = "2048",
            ["Lakona:Observability:Diagnostics:EventBuffer:MinimumLevel"] = "Error",
            ["Lakona:Observability:Metrics:Prometheus:Enabled"] = "true",
            ["Lakona:Observability:Metrics:Prometheus:Path"] = "/metrics",
            ["Lakona:Observability:Tracing:Export:Enabled"] = "true",
            ["Lakona:Observability:Tracing:Export:SampleRate"] = "0.25"
        });

        var options = LakonaObservabilityOptions.FromConfiguration(configuration);

        Assert.False(options.Logging.Enabled);
        Assert.Equal(LogLevel.Debug, options.Logging.MinimumLevel);
        Assert.Equal("Debug", options.Logging.MinimumLevelRaw);
        Assert.Equal("Trace", options.Logging.Categories["Lakona.Game.Server"]);
        Assert.False(options.Logging.Console.Enabled);
        Assert.Equal("json", options.Logging.Console.Format);
        Assert.True(options.Logging.Console.IncludeScopes);
        Assert.True(options.Logging.File.Enabled);
        Assert.Equal("logs/custom-.log", options.Logging.File.Path);
        Assert.Equal("Hour", options.Logging.File.RollingInterval);
        Assert.Equal(3, options.Logging.File.RetainedFileCount);
        Assert.Equal(64, options.Logging.File.FileSizeLimitMB);

        Assert.True(options.LocalAdmin.Enabled);
        Assert.True(options.LocalAdmin.EffectiveEnabled);
        Assert.Equal("0.0.0.0", options.LocalAdmin.Host);
        Assert.Equal(20100, options.LocalAdmin.Port);
        Assert.False(options.LocalAdmin.RequireLoopback);

        Assert.True(options.Diagnostics.DetailEnabled);
        Assert.False(options.Diagnostics.EventBuffer.Enabled);
        Assert.Equal(2048, options.Diagnostics.EventBuffer.Capacity);
        Assert.Equal(LogLevel.Error, options.Diagnostics.EventBuffer.MinimumLevel);

        Assert.True(options.Metrics.Prometheus.Enabled);
        Assert.Equal("/metrics", options.Metrics.Prometheus.Path);
        Assert.True(options.Tracing.Export.Enabled);
        Assert.Equal(0.25, options.Tracing.Export.SampleRate);
    }

    [Fact]
    public void FromConfiguration_preserves_logging_minimum_and_raw_category_levels()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Observability:Logging:MinimumLevel"] = "Warning",
            ["Lakona:Observability:Logging:Categories:Lakona.Game.Server"] = "Debug",
            ["Lakona:Observability:Logging:Categories:Lakona.Game.Custom"] = "InvalidLevel"
        });

        var options = LakonaObservabilityOptions.FromConfiguration(configuration);

        Assert.Equal(LogLevel.Warning, options.Logging.MinimumLevel);
        Assert.Equal("Warning", options.Logging.MinimumLevelRaw);
        Assert.Equal("Debug", options.Logging.Categories["Lakona.Game.Server"]);
        Assert.Equal("InvalidLevel", options.Logging.Categories["Lakona.Game.Custom"]);
    }

    [Fact]
    public void LoggingConfiguration_clears_providers_when_logging_is_disabled()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.AddProvider(new TestLoggerProvider());
            LakonaLoggingConfiguration.Apply(
                logging,
                new LakonaLoggingObservabilityOptions
                {
                    Enabled = false
                });
        });

        using var provider = services.BuildServiceProvider();

        Assert.Empty(provider.GetServices<ILoggerProvider>());
    }

    [Fact]
    public void LoggingConfiguration_does_not_add_console_provider_when_console_is_disabled()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            LakonaLoggingConfiguration.Apply(
                logging,
                new LakonaLoggingObservabilityOptions
                {
                    Console = new LakonaConsoleLoggingObservabilityOptions
                    {
                        Enabled = false
                    }
                });
        });

        using var provider = services.BuildServiceProvider();

        Assert.Empty(provider.GetServices<ILoggerProvider>());
    }

    [Fact]
    public void LoggingConfiguration_falls_back_to_information_for_invalid_category_levels()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            LakonaLoggingConfiguration.Apply(
                logging,
                new LakonaLoggingObservabilityOptions
                {
                    Console = new LakonaConsoleLoggingObservabilityOptions
                    {
                        Enabled = false
                    },
                    Categories = new Dictionary<string, string>
                    {
                        ["Lakona.Game.Custom"] = "InvalidLevel",
                        ["Lakona.Game.Numeric"] = "999"
                    }
                });
        });

        using var provider = services.BuildServiceProvider();
        var filterOptions = provider.GetRequiredService<IOptions<LoggerFilterOptions>>().Value;

        Assert.Contains(
            filterOptions.Rules,
            rule => string.Equals(rule.CategoryName, "Lakona.Game.Custom", StringComparison.Ordinal)
                && rule.LogLevel == LogLevel.Information);
        Assert.Contains(
            filterOptions.Rules,
            rule => string.Equals(rule.CategoryName, "Lakona.Game.Numeric", StringComparison.Ordinal)
                && rule.LogLevel == LogLevel.Information);
    }

    [Fact]
    public void Trace_export_options_expose_only_task_one_schema()
    {
        var propertyNames = typeof(LakonaTraceExportObservabilityOptions)
            .GetProperties()
            .Select(static property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Enabled", "SampleRate"], propertyNames);
    }

    [Theory]
    [InlineData("Verbose")]
    [InlineData("999")]
    [InlineData("")]
    [InlineData("   ")]
    public void FromConfiguration_preserves_invalid_raw_logging_minimum_level(string minimumLevel)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Observability:Logging:MinimumLevel"] = minimumLevel
        });

        var options = LakonaObservabilityOptions.FromConfiguration(configuration);

        Assert.Equal(LogLevel.Information, options.Logging.MinimumLevel);
        Assert.Equal(minimumLevel, options.Logging.MinimumLevelRaw);
    }

    [Fact]
    public void Trace_export_sample_rate_uses_invariant_decimal_format()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUICulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            var configuration = BuildConfiguration(new Dictionary<string, string?>
            {
                ["Lakona:Observability:Tracing:Export:SampleRate"] = "0.25"
            });

            var options = LakonaObservabilityOptions.FromConfiguration(configuration);

            Assert.Equal(0.25, options.Tracing.Export.SampleRate);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
        }
    }

    [Fact]
    public void RuntimeOptions_default_constructor_uses_operational_observability_defaults()
    {
        var options = new LakonaGameRuntimeOptions();

        Assert.False(options.Observability.LocalAdmin.EffectiveEnabled);
    }

    [Fact]
    public void Capabilities_aggregate_observability_marker_services()
    {
        ILakonaObservabilityCapability[] services =
        [
            new FileLoggingObservabilityCapability(),
            new OpenTelemetryObservabilityCapability(),
            new PrometheusEndpointObservabilityCapability()
        ];

        var capabilities = LakonaObservabilityCapabilities.FromServices(services);

        Assert.True(capabilities.FileLoggingIntegrationRegistered);
        Assert.True(capabilities.OpenTelemetryIntegrationRegistered);
        Assert.True(capabilities.PrometheusEndpointRegistered);
    }

    [Fact]
    public void Capabilities_default_to_no_registered_integrations()
    {
        var capabilities = new LakonaObservabilityCapabilities();

        Assert.False(capabilities.FileLoggingIntegrationRegistered);
        Assert.False(capabilities.OpenTelemetryIntegrationRegistered);
        Assert.False(capabilities.PrometheusEndpointRegistered);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private sealed class TestLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        }

        public void Dispose()
        {
        }
    }
}
