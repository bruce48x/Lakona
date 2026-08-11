using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Globalization;
using Xunit;

namespace Lakona.Game.Server.Tests.Observability;

public sealed class LakonaObservabilityOptionsTests
{
    [Fact]
    public void Observability_options_do_not_own_logging_provider_configuration()
    {
        Assert.Null(typeof(LakonaObservabilityOptions).GetProperty("Logging"));
    }

    [Fact]
    public void LocalAdmin_options_do_not_own_shared_listener_address()
    {
        Assert.Null(typeof(LakonaLocalAdminObservabilityOptions).GetProperty("Host"));
        Assert.Null(typeof(LakonaLocalAdminObservabilityOptions).GetProperty("Port"));
    }

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
            ["Lakona:Observability:LocalAdmin:Enabled"] = "true",
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

        Assert.True(options.LocalAdmin.Enabled);
        Assert.True(options.LocalAdmin.EffectiveEnabled);
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
    public void Trace_export_options_expose_only_task_one_schema()
    {
        var propertyNames = typeof(LakonaTraceExportObservabilityOptions)
            .GetProperties()
            .Select(static property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Enabled", "SampleRate"], propertyNames);
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
            new OpenTelemetryObservabilityCapability(),
            new PrometheusEndpointObservabilityCapability()
        ];

        var capabilities = LakonaObservabilityCapabilities.FromServices(services);

        Assert.True(capabilities.OpenTelemetryIntegrationRegistered);
        Assert.True(capabilities.PrometheusEndpointRegistered);
    }

    [Fact]
    public void Capabilities_default_to_no_registered_integrations()
    {
        var capabilities = new LakonaObservabilityCapabilities();

        Assert.False(capabilities.OpenTelemetryIntegrationRegistered);
        Assert.False(capabilities.PrometheusEndpointRegistered);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
