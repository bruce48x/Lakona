using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Guardrails.Rules;
using Lakona.Game.Server.Health;
using Lakona.Game.Server.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Lakona.Game.Server.Tests.Health;

public sealed class LakonaGameReadinessEvaluatorTests
{
    [Fact]
    public void Evaluate_uses_default_cluster_endpoint_when_cluster_is_not_configured()
    {
        var runtime = RuntimeDefaults();
        var snapshot = CreateEvaluator(runtime).Evaluate();

        Assert.DoesNotContain(snapshot.Diagnostics, static diagnostic => diagnostic.Code == "LAKONA040");
    }

    [Fact]
    public void Evaluate_does_not_enable_local_admin_by_default()
    {
        var runtime = RuntimeFromConfiguration(
            new Dictionary<string, string?>
            {
                ["Lakona:Management:Http:Host"] = "0.0.0.0"
            });

        Assert.False(runtime.Management.Admin.Enabled);

        var snapshot = CreateEvaluator(runtime).Evaluate();

        Assert.DoesNotContain(snapshot.Diagnostics, static diagnostic => diagnostic.Code == "LAKONA130");
    }

    [Fact]
    public void Evaluate_stays_not_ready_until_framework_startup_completes()
    {
        var runtime = RuntimeDefaults();
        var readiness = new LakonaServerReadinessState();
        var evaluator = CreateEvaluator(runtime, serverReadiness: readiness);

        var snapshot = evaluator.Evaluate();

        Assert.False(snapshot.Succeeded);
        Assert.Contains(
            snapshot.Diagnostics,
            static diagnostic => diagnostic.Code == LakonaServerReadinessState.PendingCode);
    }

    [Fact]
    public void Evaluate_becomes_ready_only_after_server_state_is_marked_ready()
    {
        var runtime = RuntimeDefaults();
        var readiness = new LakonaServerReadinessState();
        var evaluator = CreateEvaluator(runtime, serverReadiness: readiness);

        readiness.MarkReady();
        var snapshot = evaluator.Evaluate();

        Assert.True(snapshot.Succeeded);
        Assert.DoesNotContain(
            snapshot.Diagnostics,
            static diagnostic => diagnostic.Code is
                LakonaServerReadinessState.PendingCode
                or LakonaServerReadinessState.FailedCode
                or LakonaServerReadinessState.StoppingCode);
    }

    [Fact]
    public void Evaluate_returns_not_ready_during_shutdown()
    {
        var runtime = RuntimeDefaults();
        var readiness = new LakonaServerReadinessState();
        var evaluator = CreateEvaluator(runtime, serverReadiness: readiness);

        readiness.MarkReady();
        readiness.MarkStopping();
        var snapshot = evaluator.Evaluate();

        Assert.False(snapshot.Succeeded);
        Assert.Contains(
            snapshot.Diagnostics,
            static diagnostic => diagnostic.Code == LakonaServerReadinessState.StoppingCode);
    }

    [Fact]
    public void Evaluate_tracks_distributed_work_admission_authority()
    {
        var runtime = RuntimeDefaults();
        var readiness = new LakonaServerReadinessState();
        var gate = new DistributedWorkAdmissionGate();
        readiness.MarkReady();
        var evaluator = CreateEvaluator(runtime, readiness, gate);

        var fenced = evaluator.Evaluate();
        gate.Open();
        var authoritative = evaluator.Evaluate();

        Assert.False(fenced.Succeeded);
        Assert.Contains(fenced.Diagnostics, static diagnostic =>
            diagnostic.Code == LakonaGameReadinessEvaluator.DistributedAdmissionClosedCode);
        Assert.True(authoritative.Succeeded);
    }

    [Fact]
    public void Evaluate_preserves_module_failure_when_cleanup_enters_stopping_state()
    {
        var runtime = RuntimeDefaults();
        var readiness = new LakonaServerReadinessState();
        var evaluator = CreateEvaluator(runtime, serverReadiness: readiness);

        readiness.MarkFailed(
            typeof(LakonaGameReadinessEvaluatorTests),
            new InvalidOperationException("connection refused"));
        readiness.MarkStopping();
        var snapshot = evaluator.Evaluate();

        Assert.False(snapshot.Succeeded);
        Assert.Contains(
            snapshot.Diagnostics,
            static diagnostic =>
                diagnostic.Code == LakonaServerReadinessState.FailedCode
                && diagnostic.Message.Contains(
                    "connection refused",
                    StringComparison.Ordinal));
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
                ["Lakona:Health:Enabled"] = "true",
                ["Lakona:Management:Http:Host"] = "0.0.0.0",
                ["Lakona:Management:Admin:Enabled"] = "true",
                ["Lakona:Management:Admin:RequireLoopback"] = "true"
            });

        var snapshot = CreateEvaluator(runtime).Evaluate();

        Assert.Contains(snapshot.Diagnostics, static diagnostic => diagnostic.Code == "LAKONA130");
    }

    private static LakonaGameReadinessEvaluator CreateEvaluator(
        LakonaGameRuntimeOptions runtime,
        LakonaServerReadinessState? serverReadiness = null,
        DistributedWorkAdmissionGate? admissionGate = null)
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
            new LakonaHealthReadinessState(hotfixPath),
            CreateRuntimeValidator(),
            serverReadiness,
            admissionGate);
    }

    private static LakonaGameRuntimeOptions RuntimeDefaults()
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
            ]
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
            new NodeRoleConfigurationRule(),
            new ManagementAdminRule()
        ]);
    }

}
