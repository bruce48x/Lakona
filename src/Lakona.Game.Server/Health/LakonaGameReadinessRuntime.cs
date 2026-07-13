using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Observability;

namespace Lakona.Game.Server.Health;

internal static class LakonaGameReadinessRuntime
{
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
            ClusterEndpoint: new LakonaGameResolvedClusterEndpoint(
                new LakonaGameResolvedValue<string>(
                    runtime.Cluster.Endpoint,
                    LakonaGameValueSource.Configuration,
                    "Lakona:Cluster:Endpoint"),
                new LakonaGameResolvedValue<string>(
                    runtime.Cluster.Serializer,
                    LakonaGameValueSource.Configuration,
                    "Lakona:Cluster:Serializer"),
                runtime.Cluster.Seeds),
            Hotfix: new LakonaGameResolvedHotfix(
                AssemblyPath: new LakonaGameResolvedValue<string>(hotfixAssemblyPath, LakonaGameValueSource.GeneratedConvention),
                AssemblyFileName: new LakonaGameResolvedValue<string>("Server.Hotfix.dll", LakonaGameValueSource.GeneratedConvention)),
            ReliablePush: new LakonaGameResolvedReliablePush(
                StorageMode: new LakonaGameResolvedValue<string>("InMemory", LakonaGameValueSource.Default),
                PendingLimit: new LakonaGameResolvedValue<int>(
                    runtime.ReliablePush.MaxPendingPerSession,
                    LakonaGameValueSource.Configuration,
                    "Lakona:ReliablePush:MaxPendingPerSession"),
                ResumeWindowSeconds: new LakonaGameResolvedValue<int>(
                    checked((int)runtime.Sessions.ResumeWindow.TotalSeconds),
                    LakonaGameValueSource.Configuration,
                    "Lakona:Sessions:ResumeWindowSeconds"),
                HasSessionIdentityResolver: true),
            Heartbeat: new LakonaGameResolvedHeartbeat(
                Interval: new LakonaGameResolvedValue<TimeSpan>(
                    runtime.Heartbeat.Interval,
                    LakonaGameValueSource.Configuration,
                    "Lakona:Heartbeat:Interval"),
                Timeout: new LakonaGameResolvedValue<TimeSpan>(
                    runtime.Heartbeat.Timeout,
                    LakonaGameValueSource.Configuration,
                    "Lakona:Heartbeat:Timeout")),
            Observability: ToResolvedObservability(
                runtime.Observability,
                runtime.Management.Http.Host,
                observabilityCapabilities),
            ActorHosts: runtime.ActorHosts
                .Select((actor, index) => new LakonaGameResolvedValue<string>(
                    actor,
                    LakonaGameValueSource.Configuration,
                    $"Lakona:ActorHosts:{index}"))
                .ToArray());
    }

    private static LakonaGameResolvedObservability ToResolvedObservability(
        LakonaObservabilityOptions observability,
        string localHttpHost,
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
            ManagementHttpHost: new LakonaGameResolvedValue<string>(
                localHttpHost,
                LakonaGameValueSource.Configuration,
                "Lakona:Management:Http:Host"),
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
