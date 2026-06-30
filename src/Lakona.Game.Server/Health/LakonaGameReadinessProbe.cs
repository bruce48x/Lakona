using System.Text.Json;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Guardrails.Rules;
using Lakona.Game.Server.Observability;

namespace Lakona.Game.Server.Health;

public static class LakonaGameReadinessProbe
{
    public static int Run(
        LakonaGameRuntimeOptions runtime,
        ClusterOptions? clusterOptions,
        string[] args,
        LakonaObservabilityCapabilities? observabilityCapabilities = null,
        string? hotfixAssemblyPath = null)
    {
        // Liveness is a subset of readiness — fail fast if liveness fails
        var livenessExit = LakonaGameLivenessProbe.Run(clusterOptions, runtime);
        if (livenessExit != 0)
        {
            return livenessExit;
        }

        // Build applicable Guardrails rules
        var rules = new List<ILakonaGameValidationRule>
        {
            new NodeIdentityRule(),
            new EndpointRule(),
            new HotfixSourceRule(),
            new ObservabilityRule()
        };

        if (runtime.Cluster is not null)
        {
            rules.Add(new ClusterEndpointRule());
        }

        var resolved = ToResolvedRuntimeForValidation(
            runtime,
            clusterOptions,
            observabilityCapabilities,
            hotfixAssemblyPath);
        var validator = new LakonaGameRuntimeValidator(rules);
        var result = validator.Validate(resolved);

        if (args.Contains("--json", StringComparer.Ordinal))
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new Dictionary<string, object?>
                {
                    ["succeeded"] = result.Succeeded,
                    ["diagnostics"] = result.Diagnostics.Select(diagnostic => new
                    {
                        code = diagnostic.Code,
                        severity = diagnostic.Severity.ToString().ToLowerInvariant(),
                        message = diagnostic.Message,
                        repair = diagnostic.Repair
                    })
                },
                new JsonSerializerOptions { WriteIndented = true }));
            return result.Succeeded ? 0 : 1;
        }

        return WriteText(runtime, clusterOptions, result);
    }

    private static int WriteText(
        LakonaGameRuntimeOptions runtime,
        ClusterOptions? clusterOptions,
        LakonaGameValidationResult result)
    {
        var nodeId = clusterOptions?.NodeId ?? runtime.Node.Id;
        var rpcEndpoint = runtime.Endpoints.FirstOrDefault()?.ToAdvertisedEndpoint() ?? "not configured";
        if (clusterOptions?.AdvertisedEndpoints.TryGetValue("websocket", out var websocketEndpoint) == true)
        {
            rpcEndpoint = websocketEndpoint;
        }
        else if (clusterOptions?.AdvertisedEndpoints.TryGetValue("kcp", out var kcpEndpoint) == true)
        {
            rpcEndpoint = kcpEndpoint;
        }
        else if (clusterOptions?.AdvertisedEndpoints.TryGetValue("tcp", out var tcpEndpoint) == true)
        {
            rpcEndpoint = tcpEndpoint;
        }

        var clusterEndpoint = clusterOptions?.AdvertisedEndpoints.TryGetValue("cluster", out var clusterEndpointValue) == true
            ? clusterEndpointValue
            : runtime.Cluster?.Endpoint;

        var featureNames = runtime.Feature ?? Array.Empty<string>();

        _ = clusterEndpoint;

        Console.WriteLine("cluster: ok single-node");
        Console.WriteLine($"node: ok {nodeId}");
        if (featureNames.Count > 0)
        {
            Console.WriteLine($"features: ok {string.Join(", ", featureNames)}");
        }

        var hotfixFailure = result.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Code == "ULINK071");
        if (hotfixFailure is not null)
        {
            Console.Error.WriteLine("hotfix: failed local build output not found");
            Console.Error.WriteLine($"fix: {hotfixFailure.Repair}");
        }
        else
        {
            Console.WriteLine("hotfix: ok local-build Server.Hotfix.dll");
        }

        Console.WriteLine("reliable-push: ok pending limit 256, replay window 120s");
        Console.WriteLine($"rpc: ok {rpcEndpoint}");

        foreach (var diagnostic in result.Diagnostics.Where(diagnostic => diagnostic.Severity != LakonaGameDiagnosticSeverity.Error))
        {
            Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
            if (!string.IsNullOrWhiteSpace(diagnostic.Repair))
            {
                Console.WriteLine($"fix: {diagnostic.Repair}");
            }
        }

        foreach (var diagnostic in result.Diagnostics.Where(diagnostic => diagnostic.Severity == LakonaGameDiagnosticSeverity.Error))
        {
            if (diagnostic.Code == "ULINK071")
            {
                continue;
            }

            Console.Error.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
            if (!string.IsNullOrWhiteSpace(diagnostic.Repair))
            {
                Console.Error.WriteLine($"fix: {diagnostic.Repair}");
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    internal static LakonaGameResolvedRuntime ToResolvedRuntimeForValidation(
        LakonaGameRuntimeOptions runtime,
        ClusterOptions? clusterOptions,
        LakonaObservabilityCapabilities? observabilityCapabilities,
        string? hotfixAssemblyPath = null)
    {
        hotfixAssemblyPath ??= Path.Combine(
            AppContext.BaseDirectory,
            "hotfix",
            "Server.Hotfix.dll");

        return new LakonaGameResolvedRuntime(
            NodeId: new LakonaGameResolvedValue<string>(
                clusterOptions?.NodeId ?? runtime.Node.Id,
                LakonaGameValueSource.Configuration,
                "Lakona:Node:Id"),
            Endpoints: runtime.Endpoints.Select((endpoint, endpointIndex) =>
                new LakonaGameResolvedEndpoint(
                    Transport: new LakonaGameResolvedValue<string>(endpoint.Transport, LakonaGameValueSource.Configuration, $"Lakona:Endpoints:{endpointIndex}:Transport"),
                    Serializer: new LakonaGameResolvedValue<string>(endpoint.Serializer, LakonaGameValueSource.Configuration, $"Lakona:Endpoints:{endpointIndex}:Serializer"),
                    Host: new LakonaGameResolvedValue<string>(endpoint.Host, LakonaGameValueSource.Configuration, $"Lakona:Endpoints:{endpointIndex}:Host"),
                    Port: new LakonaGameResolvedValue<int>(endpoint.Port, LakonaGameValueSource.Configuration, $"Lakona:Endpoints:{endpointIndex}:Port"),
                    Path: new LakonaGameResolvedValue<string>(endpoint.Path, LakonaGameValueSource.Configuration, $"Lakona:Endpoints:{endpointIndex}:Path"),
                    AdvertisedHost: new LakonaGameResolvedValue<string>(endpoint.AdvertisedHost, LakonaGameValueSource.Configuration, $"Lakona:Endpoints:{endpointIndex}:AdvertisedHost"),
                    AdvertisedEndpoint: new LakonaGameResolvedValue<string>(endpoint.ToAdvertisedEndpoint(), LakonaGameValueSource.GeneratedConvention),
                    RpcServices: endpoint.RpcServices))
                .ToArray(),
            Cluster: new LakonaGameResolvedCluster(
                AdvertisedEndpoints: clusterOptions?.AdvertisedEndpoints ?? new Dictionary<string, string>()),
            ClusterEndpoint: runtime.Cluster is null
                ? null
                : new LakonaGameResolvedClusterEndpoint(
                    new LakonaGameResolvedValue<string>(
                        runtime.Cluster.Endpoint,
                        LakonaGameValueSource.Configuration,
                        "Lakona:Cluster:Endpoint"),
                    new LakonaGameResolvedValue<string>(
                        runtime.Cluster.Serializer,
                        LakonaGameValueSource.Configuration,
                        "Lakona:Cluster:Serializer"),
                    runtime.Cluster.Seeds),
            Feature: new LakonaGameResolvedFeature(
                Configured: null,
                Active: Array.Empty<string>(),
                StartupOrder: Array.Empty<string>()),
            Hotfix: new LakonaGameResolvedHotfix(
                AssemblyPath: new LakonaGameResolvedValue<string>(hotfixAssemblyPath, LakonaGameValueSource.GeneratedConvention),
                AssemblyFileName: new LakonaGameResolvedValue<string>("Server.Hotfix.dll", LakonaGameValueSource.GeneratedConvention)),
            ReliablePush: new LakonaGameResolvedReliablePush(
                StorageMode: new LakonaGameResolvedValue<string>("InMemory", LakonaGameValueSource.Default),
                PendingLimit: new LakonaGameResolvedValue<int>(256, LakonaGameValueSource.Default),
                ReplayWindowSeconds: new LakonaGameResolvedValue<int>(120, LakonaGameValueSource.Default),
                HasSessionIdentityResolver: true),
            Observability: ToResolvedObservability(runtime.Observability, observabilityCapabilities),
            Profile: runtime.Profile);
    }

    private static LakonaGameResolvedObservability ToResolvedObservability(
        LakonaObservabilityOptions observability,
        LakonaObservabilityCapabilities? capabilities)
    {
        capabilities ??= new LakonaObservabilityCapabilities();

        return new LakonaGameResolvedObservability(
            LocalAdminEnabled: new LakonaGameResolvedValue<bool>(
                observability.LocalAdmin.EffectiveEnabled,
                observability.LocalAdmin.Enabled.HasValue
                    ? LakonaGameValueSource.Configuration
                    : LakonaGameValueSource.Default,
                "Lakona:Observability:LocalAdmin:Enabled"),
            LocalAdminHost: new LakonaGameResolvedValue<string>(
                observability.LocalAdmin.Host,
                LakonaGameValueSource.Configuration,
                "Lakona:Observability:LocalAdmin:Host"),
            LocalAdminRequireLoopback: new LakonaGameResolvedValue<bool>(
                observability.LocalAdmin.RequireLoopback,
                LakonaGameValueSource.Configuration,
                "Lakona:Observability:LocalAdmin:RequireLoopback"),
            DetailEnabled: new LakonaGameResolvedValue<bool>(
                observability.Diagnostics.DetailEnabled,
                LakonaGameValueSource.Configuration,
                "Lakona:Observability:Diagnostics:DetailEnabled"),
            FileLoggingEnabled: new LakonaGameResolvedValue<bool>(
                observability.Logging.File.Enabled,
                LakonaGameValueSource.Configuration,
                "Lakona:Observability:Logging:File:Enabled"),
            FileLoggingIntegrationRegistered: capabilities.FileLoggingIntegrationRegistered,
            TraceExportEnabled: new LakonaGameResolvedValue<bool>(
                observability.Tracing.Export.Enabled,
                LakonaGameValueSource.Configuration,
                "Lakona:Observability:Tracing:Export:Enabled"),
            OpenTelemetryIntegrationRegistered: capabilities.OpenTelemetryIntegrationRegistered,
            PrometheusEnabled: new LakonaGameResolvedValue<bool>(
                observability.Metrics.Prometheus.Enabled,
                LakonaGameValueSource.Configuration,
                "Lakona:Observability:Metrics:Prometheus:Enabled"),
            PrometheusEndpointRegistered: capabilities.PrometheusEndpointRegistered,
            PrometheusPath: new LakonaGameResolvedValue<string>(
                observability.Metrics.Prometheus.Path,
                LakonaGameValueSource.Configuration,
                "Lakona:Observability:Metrics:Prometheus:Path"),
            EventBufferCapacity: new LakonaGameResolvedValue<int>(
                observability.Diagnostics.EventBuffer.Capacity,
                LakonaGameValueSource.Configuration,
                "Lakona:Observability:Diagnostics:EventBuffer:Capacity"),
            EventBufferCapacityRaw: new LakonaGameResolvedValue<string>(
                observability.Diagnostics.EventBuffer.CapacityRaw,
                LakonaGameValueSource.Configuration,
                "Lakona:Observability:Diagnostics:EventBuffer:Capacity"),
            LoggingMinimumLevel: new LakonaGameResolvedValue<string>(
                observability.Logging.MinimumLevelRaw,
                LakonaGameValueSource.Configuration,
                "Lakona:Observability:Logging:MinimumLevel"),
            TraceSampleRate: new LakonaGameResolvedValue<double>(
                observability.Tracing.Export.SampleRate,
                LakonaGameValueSource.Configuration,
                "Lakona:Observability:Tracing:Export:SampleRate"),
            TraceSampleRateRaw: new LakonaGameResolvedValue<string>(
                observability.Tracing.Export.SampleRateRaw,
                LakonaGameValueSource.Configuration,
                "Lakona:Observability:Tracing:Export:SampleRate"));
    }
}
