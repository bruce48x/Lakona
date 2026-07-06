using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Health;
using Lakona.Game.Server.Observability;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Lakona.Game.Server.Tests.Health;

public sealed class LakonaGameReadinessProbeTests
{
    [Fact]
    public void Run_UsesDefaultClusterEndpointWhenClusterIsNotConfigured()
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "dev-1" },
            Endpoints =
            [
                new LakonaGameEndpointOptions
                {
                    Transport = "websocket",
                    Serializer = "json",
                    Host = "127.0.0.1",
                    Port = 20000,
                    Path = "/ws",
                    RpcServices = ["login"]
                }
            ]
        };

        var (_, output, errors) = CaptureRun(runtime, runtime.ToClusterOptions(), []);

        var text = output + errors;
        Assert.DoesNotContain("ULINK040", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona:Cluster:Endpoint is required", text, StringComparison.Ordinal);
        Assert.Contains("tcp://127.0.0.1:21001", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_JsonOutputIncludesObservabilityDiagnosticsAndRepairs()
    {
        var runtime = RuntimeWithObservability(new LakonaObservabilityOptions
        {
            Logging = new LakonaLoggingObservabilityOptions
            {
                File = new LakonaFileLoggingObservabilityOptions { Enabled = true }
            }
        });

        var (exitCode, output, errors) = CaptureRun(
            runtime,
            runtime.ToClusterOptions(),
            ["--json"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("\"code\": \"ULINK133\"", output, StringComparison.Ordinal);
        Assert.Contains("Lakona:Observability:Logging:File:Enabled", output, StringComparison.Ordinal);
        Assert.Equal("", errors);
    }

    [Fact]
    public void Run_TextOutputIncludesObservabilityDiagnosticsAndRepairs()
    {
        EnsureHotfixAssemblyExists();
        var runtime = RuntimeWithObservability(new LakonaObservabilityOptions
        {
            Metrics = new LakonaMetricsObservabilityOptions
            {
                Prometheus = new LakonaPrometheusObservabilityOptions
                {
                    Enabled = true,
                    Path = "metrics"
                }
            }
        });

        var (exitCode, output, errors) = CaptureRun(
            runtime,
            runtime.ToClusterOptions(),
            []);

        Assert.Equal(1, exitCode);
        Assert.Contains("rpc: ok", output, StringComparison.Ordinal);
        Assert.Contains("ULINK135", errors, StringComparison.Ordinal);
        Assert.Contains("ULINK136", errors, StringComparison.Ordinal);
        Assert.Contains("fix: Use an absolute non-root path such as /_lakona/metrics without query or fragment.", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_TextOutputIncludesObservabilityDiagnosticsWhenHotfixAlsoFails()
    {
        try
        {
            DeleteHotfixAssembly();
            var runtime = RuntimeWithObservability(new LakonaObservabilityOptions
            {
                Metrics = new LakonaMetricsObservabilityOptions
                {
                    Prometheus = new LakonaPrometheusObservabilityOptions
                    {
                        Path = "metrics"
                    }
                }
            });

            var (exitCode, output, errors) = CaptureRun(
                runtime,
                runtime.ToClusterOptions(),
                []);

            Assert.Equal(1, exitCode);
            Assert.Contains("cluster: ok tcp://127.0.0.1:21001", output, StringComparison.Ordinal);
            Assert.Contains("hotfix: failed local build output not found", errors, StringComparison.Ordinal);
            Assert.Contains("ULINK136", errors, StringComparison.Ordinal);
            Assert.Contains("fix: Use an absolute non-root path such as /_lakona/metrics without query or fragment.", errors, StringComparison.Ordinal);
        }
        finally
        {
            EnsureHotfixAssemblyExists();
        }
    }

    [Theory]
    [InlineData("Verbose")]
    [InlineData("")]
    [InlineData("   ")]
    public void Run_JsonOutputIncludesInvalidConfiguredLoggingMinimumLevel(string minimumLevel)
    {
        var runtime = RuntimeFromConfiguration(
            new Dictionary<string, string?>
            {
                ["Lakona:Endpoints:0:Transport"] = "websocket",
                ["Lakona:Endpoints:0:Serializer"] = "json",
                ["Lakona:Endpoints:0:Host"] = "127.0.0.1",
                ["Lakona:Endpoints:0:Port"] = "20000",
                ["Lakona:Endpoints:0:Path"] = "/ws",
                ["Lakona:Observability:Logging:MinimumLevel"] = minimumLevel
            });

        var (exitCode, output, errors) = CaptureRun(
            runtime,
            runtime.ToClusterOptions(),
            ["--json"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("\"code\": \"ULINK138\"", output, StringComparison.Ordinal);
        Assert.Contains("Lakona:Observability:Logging:MinimumLevel", output, StringComparison.Ordinal);
        Assert.Equal("", errors);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("999999999999999999999999")]
    public void Run_JsonOutputIncludesInvalidConfiguredEventBufferCapacity(string capacity)
    {
        var runtime = RuntimeFromConfiguration(
            new Dictionary<string, string?>
            {
                ["Lakona:Endpoints:0:Transport"] = "websocket",
                ["Lakona:Endpoints:0:Serializer"] = "json",
                ["Lakona:Endpoints:0:Host"] = "127.0.0.1",
                ["Lakona:Endpoints:0:Port"] = "20000",
                ["Lakona:Endpoints:0:Path"] = "/ws",
                ["Lakona:Observability:Diagnostics:EventBuffer:Capacity"] = capacity
            });

        var (exitCode, output, errors) = CaptureRun(
            runtime,
            runtime.ToClusterOptions(),
            ["--json"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("\"code\": \"ULINK137\"", output, StringComparison.Ordinal);
        Assert.Contains("Lakona:Observability:Diagnostics:EventBuffer:Capacity", output, StringComparison.Ordinal);
        Assert.Equal("", errors);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("not-a-number")]
    public void Run_JsonOutputIncludesInvalidConfiguredTraceSampleRate(string sampleRate)
    {
        var runtime = RuntimeFromConfiguration(
            new Dictionary<string, string?>
            {
                ["Lakona:Endpoints:0:Transport"] = "websocket",
                ["Lakona:Endpoints:0:Serializer"] = "json",
                ["Lakona:Endpoints:0:Host"] = "127.0.0.1",
                ["Lakona:Endpoints:0:Port"] = "20000",
                ["Lakona:Endpoints:0:Path"] = "/ws",
                ["Lakona:Observability:Tracing:Export:SampleRate"] = sampleRate
            });

        var (exitCode, output, errors) = CaptureRun(
            runtime,
            runtime.ToClusterOptions(),
            ["--json"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("\"code\": \"ULINK139\"", output, StringComparison.Ordinal);
        Assert.Contains("Lakona:Observability:Tracing:Export:SampleRate", output, StringComparison.Ordinal);
        Assert.Equal("", errors);
    }

    [Fact]
    public void Run_DoesNotEnableLocalAdminByDefault()
    {
        var runtime = RuntimeFromConfiguration(
            new Dictionary<string, string?>
            {
                ["Lakona:Observability:LocalAdmin:Host"] = "0.0.0.0",
                ["Lakona:Observability:Diagnostics:DetailEnabled"] = "true"
            });

        Assert.False(runtime.Observability.LocalAdmin.EffectiveEnabled);

        var (_, output, errors) = CaptureRun(
            runtime,
            runtime.ToClusterOptions(),
            ["--json"]);

        var text = output + errors;
        Assert.DoesNotContain("ULINK130", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ULINK132", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_DoesNotEnableLocalAdminByDefaultWhenLocalAdminHostIsConfigured()
    {
        var runtime = RuntimeFromConfiguration(
            new Dictionary<string, string?>
            {
                ["Lakona:Observability:LocalAdmin:Host"] = "0.0.0.0",
                ["Lakona:Observability:Diagnostics:DetailEnabled"] = "true"
            });

        Assert.False(runtime.Observability.LocalAdmin.EffectiveEnabled);

        var (_, output, errors) = CaptureRun(
            runtime,
            runtime.ToClusterOptions(),
            ["--json"]);

        var text = output + errors;
        Assert.DoesNotContain("ULINK130", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ULINK132", text, StringComparison.Ordinal);
    }

    private static LakonaGameRuntimeOptions RuntimeWithObservability(
        LakonaObservabilityOptions observability)
    {
        return new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "dev-1" },
            Endpoints =
            [
                new LakonaGameEndpointOptions
                {
                    Transport = "websocket",
                    Serializer = "json",
                    Host = "127.0.0.1",
                    Port = 20000,
                    Path = "/ws",
                    RpcServices = ["login"]
                }
            ],
            Observability = observability
        };
    }

    private static LakonaGameRuntimeOptions RuntimeFromConfiguration(
        Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return LakonaGameRuntimeOptions.FromConfiguration(configuration);
    }

    private static (int ExitCode, string Output, string Errors) CaptureRun(
        LakonaGameRuntimeOptions runtime,
        ClusterOptions clusterOptions,
        string[] args)
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var originalOutput = Console.Out;
        var originalError = Console.Error;

        try
        {
            Console.SetOut(output);
            Console.SetError(errors);

            var exitCode = LakonaGameReadinessProbe.Run(runtime, clusterOptions, args);
            return (exitCode, output.ToString(), errors.ToString());
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }
    }

    private static void EnsureHotfixAssemblyExists()
    {
        var hotfixPath = Path.Combine(AppContext.BaseDirectory, "hotfix", "Server.Hotfix.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(hotfixPath)!);
        File.WriteAllText(hotfixPath, "");
    }

    private static void DeleteHotfixAssembly()
    {
        var hotfixPath = Path.Combine(AppContext.BaseDirectory, "hotfix", "Server.Hotfix.dll");
        if (File.Exists(hotfixPath))
        {
            File.Delete(hotfixPath);
        }
    }
}
