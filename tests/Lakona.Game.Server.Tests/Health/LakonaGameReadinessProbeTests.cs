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
    public void Run_DoesNotRequireClusterEndpointWhenClusterIsNotConfigured()
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
            ],
            Cluster = null
        };

        var output = new StringWriter();
        var errors = new StringWriter();
        var originalOutput = Console.Out;
        var originalError = Console.Error;

        try
        {
            Console.SetOut(output);
            Console.SetError(errors);

            _ = LakonaGameReadinessProbe.Run(runtime, runtime.ToClusterOptions(), ["--json"]);
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }

        var text = output.ToString() + errors.ToString();
        Assert.DoesNotContain("ULINK040", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona:Cluster:Endpoint is required", text, StringComparison.Ordinal);
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
        Assert.Contains("fix: Lakona:Observability:Metrics:Prometheus:Path", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_TextOutputIncludesObservabilityDiagnosticsWhenHotfixAlsoFails()
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
        Assert.Contains("cluster: ok single-node", output, StringComparison.Ordinal);
        Assert.Contains("hotfix: failed local build output not found", errors, StringComparison.Ordinal);
        Assert.Contains("ULINK136", errors, StringComparison.Ordinal);
        Assert.Contains("fix: Lakona:Observability:Metrics:Prometheus:Path", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_JsonOutputIncludesInvalidConfiguredLoggingMinimumLevel()
    {
        var runtime = RuntimeFromConfiguration(
            new Dictionary<string, string?>
            {
                ["Lakona:Endpoints:0:Transport"] = "websocket",
                ["Lakona:Endpoints:0:Serializer"] = "json",
                ["Lakona:Endpoints:0:Host"] = "127.0.0.1",
                ["Lakona:Endpoints:0:Port"] = "20000",
                ["Lakona:Endpoints:0:Path"] = "/ws",
                ["Lakona:Observability:Logging:MinimumLevel"] = "Verbose"
            },
            "Production");

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
    [InlineData("Production", LakonaGameRuntimeProfile.Production)]
    [InlineData("battle-1", LakonaGameRuntimeProfile.Production)]
    public void Run_DoesNotEnableLocalAdminByDefaultForProductionOrNodeNamedProfiles(
        string environmentName,
        LakonaGameRuntimeProfile expectedProfile)
    {
        var runtime = RuntimeFromConfiguration(
            new Dictionary<string, string?>
            {
                ["Lakona:Observability:LocalAdmin:Host"] = "0.0.0.0",
                ["Lakona:Observability:Diagnostics:DetailEnabled"] = "true"
            },
            environmentName);

        Assert.Equal(expectedProfile, runtime.Profile);
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
    public void Run_DoesNotEnableLocalAdminByDefaultForComposeProfile()
    {
        var runtime = RuntimeFromConfiguration(
            new Dictionary<string, string?>
            {
                ["Lakona:Profile"] = "Compose",
                ["Lakona:Observability:LocalAdmin:Host"] = "0.0.0.0",
                ["Lakona:Observability:Diagnostics:DetailEnabled"] = "true"
            },
            "Production");

        Assert.Equal(LakonaGameRuntimeProfile.Compose, runtime.Profile);
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
            Cluster = null,
            Observability = observability
        };
    }

    private static LakonaGameRuntimeOptions RuntimeFromConfiguration(
        Dictionary<string, string?> values,
        string? environmentName)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return LakonaGameRuntimeOptions.FromConfiguration(configuration, environmentName);
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
