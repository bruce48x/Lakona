using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Guardrails.Rules;
using Lakona.Game.Server.Health;
using Lakona.Game.Server.Observability;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Lakona.Game.Server.Tests.Health;

public sealed class LakonaGameReadinessEvaluatorTests
{
    [Fact]
    public void Evaluate_uses_default_cluster_endpoint_when_cluster_is_not_configured()
    {
        var runtime = RuntimeWithObservability(LakonaObservabilityOptions.Defaults());
        var snapshot = CreateEvaluator(runtime).Evaluate();

        Assert.DoesNotContain(snapshot.Diagnostics, static diagnostic => diagnostic.Code == "ULINK040");
    }

    [Fact]
    public void Evaluate_includes_observability_diagnostics_and_repairs()
    {
        var runtime = RuntimeWithObservability(new LakonaObservabilityOptions
        {
            Logging = new LakonaLoggingObservabilityOptions
            {
                File = new LakonaFileLoggingObservabilityOptions { Enabled = true }
            }
        });

        var snapshot = CreateEvaluator(runtime).Evaluate();

        Assert.False(snapshot.Succeeded);
        var diagnostic = Assert.Single(snapshot.Diagnostics, static diagnostic => diagnostic.Code == "ULINK133");
        Assert.Contains("Lakona:Observability:Logging:File:Enabled", diagnostic.Repair, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Verbose")]
    [InlineData("")]
    [InlineData("   ")]
    public void Evaluate_includes_invalid_configured_logging_minimum_level(string minimumLevel)
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

        var snapshot = CreateEvaluator(runtime).Evaluate();

        Assert.False(snapshot.Succeeded);
        Assert.Contains(
            snapshot.Diagnostics,
            static diagnostic => diagnostic.Code == "ULINK138"
                && diagnostic.Message.Contains(
                    "Lakona:Observability:Logging:MinimumLevel",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_does_not_enable_local_admin_by_default()
    {
        var runtime = RuntimeFromConfiguration(
            new Dictionary<string, string?>
            {
                ["Lakona:Health:Http:Host"] = "0.0.0.0",
                ["Lakona:Observability:Diagnostics:DetailEnabled"] = "true"
            });

        Assert.False(runtime.Observability.LocalAdmin.EffectiveEnabled);

        var snapshot = CreateEvaluator(runtime).Evaluate();

        Assert.DoesNotContain(snapshot.Diagnostics, static diagnostic => diagnostic.Code == "ULINK130");
        Assert.DoesNotContain(snapshot.Diagnostics, static diagnostic => diagnostic.Code == "ULINK132");
    }

    [Fact]
    public void Evaluate_uses_shared_listener_host_for_local_admin_exposure_guardrail()
    {
        var runtime = RuntimeFromConfiguration(
            new Dictionary<string, string?>
            {
                ["Lakona:Endpoints:0:Transport"] = "websocket",
                ["Lakona:Endpoints:0:Serializer"] = "json",
                ["Lakona:Endpoints:0:Host"] = "127.0.0.1",
                ["Lakona:Endpoints:0:Port"] = "20000",
                ["Lakona:Endpoints:0:Path"] = "/ws",
                ["Lakona:Health:Http:Enabled"] = "true",
                ["Lakona:Health:Http:Host"] = "0.0.0.0",
                ["Lakona:Observability:LocalAdmin:Enabled"] = "true",
                ["Lakona:Observability:LocalAdmin:RequireLoopback"] = "false",
                ["Lakona:Observability:Diagnostics:DetailEnabled"] = "true"
            });

        var snapshot = CreateEvaluator(runtime).Evaluate();

        Assert.Contains(snapshot.Diagnostics, static diagnostic => diagnostic.Code == "ULINK132");
    }

    private static LakonaGameReadinessEvaluator CreateEvaluator(
        LakonaGameRuntimeOptions runtime,
        LakonaObservabilityCapabilities? capabilities = null)
    {
        var hotfixPath = Path.Combine(
            AppContext.BaseDirectory,
            "hotfix",
            "Server.Hotfix.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(hotfixPath)!);
        File.WriteAllText(hotfixPath, "");

        return new LakonaGameReadinessEvaluator(
            runtime,
            runtime.ToClusterOptions(),
            capabilities ?? new LakonaObservabilityCapabilities(),
            new LakonaHealthReadinessState(hotfixPath),
            CreateRuntimeValidator());
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

    private static LakonaGameRuntimeValidator CreateRuntimeValidator()
    {
        return new LakonaGameRuntimeValidator(
        [
            new NodeIdentityRule(),
            new EndpointRule(),
            new ClusterEndpointRule(),
            new HotfixSourceRule(),
            new HeartbeatRule(),
            new ActorHostConfigurationRule(),
            new ObservabilityRule()
        ]);
    }
}
