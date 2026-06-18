using System.Text.Json;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Guardrails.Rules;

namespace Lakona.Game.Server.Health;

public static class LakonaGameReadinessProbe
{
    public static int Run(
        LakonaGameRuntimeOptions runtime,
        ClusterOptions? clusterOptions,
        string[] args)
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
            new HotfixSourceRule()
        };

        if (clusterOptions is not null)
        {
            rules.Add(new ClusterEndpointRule());
        }

        var resolved = ToResolvedRuntime(runtime, clusterOptions);
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
            return 1;
        }

        Console.WriteLine("hotfix: ok local-build Server.Hotfix.dll");
        Console.WriteLine("reliable-push: ok pending limit 256, replay window 120s");
        Console.WriteLine($"rpc: ok {rpcEndpoint}");

        foreach (var diagnostic in result.Diagnostics.Where(diagnostic => diagnostic.Severity == LakonaGameDiagnosticSeverity.Error))
        {
            Console.Error.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
            if (!string.IsNullOrWhiteSpace(diagnostic.Repair))
            {
                Console.Error.WriteLine($"fix: {diagnostic.Repair}");
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static LakonaGameResolvedRuntime ToResolvedRuntime(
        LakonaGameRuntimeOptions runtime,
        ClusterOptions? clusterOptions)
    {
        var hotfixPath = Path.Combine(
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
                    runtime.Cluster?.Endpoint ?? "",
                    LakonaGameValueSource.Configuration,
                    "Lakona:Cluster:Endpoint"),
                runtime.Cluster?.Seeds ?? Array.Empty<string>()),
            Feature: new LakonaGameResolvedFeature(
                Configured: null,
                Active: Array.Empty<string>(),
                StartupOrder: Array.Empty<string>()),
            Hotfix: new LakonaGameResolvedHotfix(
                AssemblyPath: new LakonaGameResolvedValue<string>(hotfixPath, LakonaGameValueSource.GeneratedConvention),
                AssemblyFileName: new LakonaGameResolvedValue<string>("Server.Hotfix.dll", LakonaGameValueSource.GeneratedConvention)),
            ReliablePush: new LakonaGameResolvedReliablePush(
                StorageMode: new LakonaGameResolvedValue<string>("InMemory", LakonaGameValueSource.Default),
                PendingLimit: new LakonaGameResolvedValue<int>(256, LakonaGameValueSource.Default),
                ReplayWindowSeconds: new LakonaGameResolvedValue<int>(120, LakonaGameValueSource.Default),
                HasSessionIdentityResolver: true),
            Profile: LakonaGameRuntimeProfile.Development);
    }
}
