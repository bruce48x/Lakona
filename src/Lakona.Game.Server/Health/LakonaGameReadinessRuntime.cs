using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Guardrails;

namespace Lakona.Game.Server.Health;

internal static class LakonaGameReadinessRuntime
{
    internal static LakonaGameResolvedRuntime ToResolvedRuntimeForValidation(
        LakonaGameRuntimeOptions runtime,
        ClusterOptions? clusterOptions,
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
                    RpcServices: endpoint.RpcServices)
                {
                    MaxActiveConnections = new LakonaGameResolvedValue<int>(
                        endpoint.ConnectionLimits.MaxActiveConnections,
                        LakonaGameValueSource.Configuration,
                        $"Lakona:Endpoints:{endpointIndex}:ConnectionLimits:MaxActiveConnections"),
                    MaxPendingHandshakes = new LakonaGameResolvedValue<int>(
                        endpoint.ConnectionLimits.MaxPendingHandshakes,
                        LakonaGameValueSource.Configuration,
                        $"Lakona:Endpoints:{endpointIndex}:ConnectionLimits:MaxPendingHandshakes"),
                    HandshakeTimeout = new LakonaGameResolvedValue<TimeSpan>(
                        endpoint.ConnectionLimits.HandshakeTimeout,
                        LakonaGameValueSource.Configuration,
                        $"Lakona:Endpoints:{endpointIndex}:ConnectionLimits:HandshakeTimeout")
                })
                .ToArray(),
            Cluster: new LakonaGameResolvedCluster(
                AdvertisedEndpoints: clusterOptions?.AdvertisedEndpoints ?? new Dictionary<string, string>()),
            ClusterEndpoint: new LakonaGameResolvedClusterEndpoint(
                new LakonaGameResolvedValue<string>(
                    runtime.Cluster.Endpoint,
                    LakonaGameValueSource.Configuration,
                    "Lakona:Cluster:Endpoint"),
                runtime.Cluster.Peers.Select(static peer => peer.Endpoint).ToArray()),
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
            Management: new LakonaGameResolvedManagement(
                AdminEnabled: new LakonaGameResolvedValue<bool>(
                    runtime.Management.Admin.Enabled,
                    LakonaGameValueSource.Configuration,
                    "Lakona:Management:Admin:Enabled"),
                HttpHost: new LakonaGameResolvedValue<string>(
                    runtime.Management.Http.Host,
                    LakonaGameValueSource.Configuration,
                    "Lakona:Management:Http:Host"),
                AdminRequireLoopback: new LakonaGameResolvedValue<bool>(
                    runtime.Management.Admin.RequireLoopback,
                    LakonaGameValueSource.Configuration,
                    "Lakona:Management:Admin:RequireLoopback")),
            ActorHosts: runtime.ActorHosts
                .Select((actor, index) => new LakonaGameResolvedValue<string>(
                    actor,
                    LakonaGameValueSource.Configuration,
                    $"Lakona:ActorHosts:{index}"))
                .ToArray());
    }

}
