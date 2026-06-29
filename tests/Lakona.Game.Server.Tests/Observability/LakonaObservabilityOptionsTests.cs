using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lakona.Game.Server.Tests.Observability;

public sealed class LakonaObservabilityOptionsTests
{
    [Theory]
    [InlineData(LakonaGameRuntimeProfile.Development, true)]
    [InlineData(LakonaGameRuntimeProfile.Compose, false)]
    [InlineData(LakonaGameRuntimeProfile.Production, false)]
    public void LocalAdmin_default_effective_enablement_is_development_only(
        LakonaGameRuntimeProfile profile,
        bool expectedEnabled)
    {
        var options = LakonaObservabilityOptions.FromConfiguration(BuildConfiguration([]), profile);

        Assert.Null(options.LocalAdmin.Enabled);
        Assert.Equal(expectedEnabled, options.LocalAdmin.EffectiveEnabled);
    }

    [Fact]
    public void FromConfiguration_uses_operational_defaults()
    {
        var options = LakonaObservabilityOptions.FromConfiguration(
            BuildConfiguration([]),
            LakonaGameRuntimeProfile.Development);

        Assert.True(options.Logging.Enabled);
        Assert.Equal(LogLevel.Information, options.Logging.MinimumLevel);
        Assert.True(options.Logging.Console.Enabled);
        Assert.Equal("compact", options.Logging.Console.Format);
        Assert.False(options.Logging.Console.IncludeScopes);
        Assert.False(options.Logging.File.Enabled);
        Assert.Equal("logs/lakona-.log", options.Logging.File.Path);
        Assert.Equal("Day", options.Logging.File.RollingInterval);
        Assert.Equal(7, options.Logging.File.RetainedFileCountLimit);
        Assert.Equal(128 * 1024 * 1024, options.Logging.File.FileSizeLimitBytes);
        Assert.Equal("Information", options.Logging.CategoryLevels["Lakona"]);

        Assert.Equal("127.0.0.1", options.LocalAdmin.Host);
        Assert.Equal(20090, options.LocalAdmin.Port);
        Assert.True(options.LocalAdmin.RequireLoopback);

        Assert.True(options.Diagnostics.Summary.Enabled);
        Assert.False(options.Diagnostics.Detail.Enabled);
        Assert.True(options.Diagnostics.EventBuffer.Enabled);
        Assert.Equal(1024, options.Diagnostics.EventBuffer.Capacity);
        Assert.Equal(LogLevel.Warning, options.Diagnostics.EventBuffer.MinimumLevel);

        Assert.False(options.Metrics.Prometheus.Enabled);
        Assert.Equal("/_lakona/metrics", options.Metrics.Prometheus.Path);

        Assert.False(options.Tracing.Export.Enabled);
        Assert.Equal(1.0, options.Tracing.Export.SampleRate);
    }

    [Fact]
    public void FromConfiguration_binds_observability_values()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Observability:Logging:Enabled"] = "false",
            ["Lakona:Observability:Logging:MinimumLevel"] = "Debug",
            ["Lakona:Observability:Logging:CategoryLevels:Lakona.Game.Server"] = "Trace",
            ["Lakona:Observability:Logging:Console:Enabled"] = "false",
            ["Lakona:Observability:Logging:Console:Format"] = "json",
            ["Lakona:Observability:Logging:Console:IncludeScopes"] = "true",
            ["Lakona:Observability:Logging:File:Enabled"] = "true",
            ["Lakona:Observability:Logging:File:Path"] = "logs/custom-.log",
            ["Lakona:Observability:Logging:File:RollingInterval"] = "Hour",
            ["Lakona:Observability:Logging:File:RetainedFileCountLimit"] = "3",
            ["Lakona:Observability:Logging:File:FileSizeLimitBytes"] = "4096",
            ["Lakona:Observability:LocalAdmin:Enabled"] = "true",
            ["Lakona:Observability:LocalAdmin:Host"] = "0.0.0.0",
            ["Lakona:Observability:LocalAdmin:Port"] = "20100",
            ["Lakona:Observability:LocalAdmin:RequireLoopback"] = "false",
            ["Lakona:Observability:Diagnostics:Summary:Enabled"] = "false",
            ["Lakona:Observability:Diagnostics:Detail:Enabled"] = "true",
            ["Lakona:Observability:Diagnostics:EventBuffer:Enabled"] = "false",
            ["Lakona:Observability:Diagnostics:EventBuffer:Capacity"] = "2048",
            ["Lakona:Observability:Diagnostics:EventBuffer:MinimumLevel"] = "Error",
            ["Lakona:Observability:Metrics:Prometheus:Enabled"] = "true",
            ["Lakona:Observability:Metrics:Prometheus:Path"] = "/metrics",
            ["Lakona:Observability:Tracing:Export:Enabled"] = "true",
            ["Lakona:Observability:Tracing:Export:Endpoint"] = "http://collector:4317",
            ["Lakona:Observability:Tracing:Export:Protocol"] = "otlp",
            ["Lakona:Observability:Tracing:Export:SampleRate"] = "0.25"
        });

        var options = LakonaObservabilityOptions.FromConfiguration(
            configuration,
            LakonaGameRuntimeProfile.Production);

        Assert.False(options.Logging.Enabled);
        Assert.Equal(LogLevel.Debug, options.Logging.MinimumLevel);
        Assert.Equal("Trace", options.Logging.CategoryLevels["Lakona.Game.Server"]);
        Assert.False(options.Logging.Console.Enabled);
        Assert.Equal("json", options.Logging.Console.Format);
        Assert.True(options.Logging.Console.IncludeScopes);
        Assert.True(options.Logging.File.Enabled);
        Assert.Equal("logs/custom-.log", options.Logging.File.Path);
        Assert.Equal("Hour", options.Logging.File.RollingInterval);
        Assert.Equal(3, options.Logging.File.RetainedFileCountLimit);
        Assert.Equal(4096, options.Logging.File.FileSizeLimitBytes);

        Assert.True(options.LocalAdmin.Enabled);
        Assert.True(options.LocalAdmin.EffectiveEnabled);
        Assert.Equal("0.0.0.0", options.LocalAdmin.Host);
        Assert.Equal(20100, options.LocalAdmin.Port);
        Assert.False(options.LocalAdmin.RequireLoopback);

        Assert.False(options.Diagnostics.Summary.Enabled);
        Assert.True(options.Diagnostics.Detail.Enabled);
        Assert.False(options.Diagnostics.EventBuffer.Enabled);
        Assert.Equal(2048, options.Diagnostics.EventBuffer.Capacity);
        Assert.Equal(LogLevel.Error, options.Diagnostics.EventBuffer.MinimumLevel);

        Assert.True(options.Metrics.Prometheus.Enabled);
        Assert.Equal("/metrics", options.Metrics.Prometheus.Path);
        Assert.True(options.Tracing.Export.Enabled);
        Assert.Equal("http://collector:4317", options.Tracing.Export.Endpoint);
        Assert.Equal("otlp", options.Tracing.Export.Protocol);
        Assert.Equal(0.25, options.Tracing.Export.SampleRate);
    }

    [Theory]
    [InlineData(null, LakonaGameRuntimeProfile.Development)]
    [InlineData("", LakonaGameRuntimeProfile.Development)]
    [InlineData("Development", LakonaGameRuntimeProfile.Development)]
    [InlineData("Compose", LakonaGameRuntimeProfile.Compose)]
    [InlineData("Production", LakonaGameRuntimeProfile.Production)]
    [InlineData("battle-1", LakonaGameRuntimeProfile.Production)]
    public void ProfileResolver_maps_host_environment_names(
        string? environmentName,
        LakonaGameRuntimeProfile expectedProfile)
    {
        var profile = LakonaGameRuntimeProfileResolver.Resolve(BuildConfiguration([]), environmentName);

        Assert.Equal(expectedProfile, profile);
    }

    [Fact]
    public void ProfileResolver_lets_configuration_override_host_environment_name()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Profile"] = "Compose"
        });

        var profile = LakonaGameRuntimeProfileResolver.Resolve(configuration, "Production");

        Assert.Equal(LakonaGameRuntimeProfile.Compose, profile);
    }

    [Fact]
    public void ProfileResolver_rejects_invalid_configured_profile()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Profile"] = "staging"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            LakonaGameRuntimeProfileResolver.Resolve(configuration, "Development"));

        Assert.Contains("Lakona:Profile", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Development, Compose, or Production", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeOptions_carry_profile_and_observability()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Profile"] = "Production"
        });

        var options = LakonaGameRuntimeOptions.FromConfiguration(configuration, "Development");

        Assert.Equal(LakonaGameRuntimeProfile.Production, options.Profile);
        Assert.False(options.Observability.LocalAdmin.EffectiveEnabled);
    }

    [Fact]
    public void Capabilities_aggregate_enabled_observability_markers()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Observability:Logging:File:Enabled"] = "true",
            ["Lakona:Observability:Metrics:Prometheus:Enabled"] = "true",
            ["Lakona:Observability:Tracing:Export:Enabled"] = "true"
        });
        var options = LakonaObservabilityOptions.FromConfiguration(
            configuration,
            LakonaGameRuntimeProfile.Production);

        var capabilities = LakonaObservabilityCapabilities.FromOptions(options);

        Assert.True(capabilities.FileLogging);
        Assert.True(capabilities.OpenTelemetry);
        Assert.True(capabilities.Prometheus);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
