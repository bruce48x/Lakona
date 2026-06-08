internal static class OperationsTemplates
{
    public static string RenderServerDockerfile()
    {
        return """
        FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
        WORKDIR /src
        COPY . .
        RUN dotnet publish Server/Server/Server.csproj -c Release -o /app

        FROM mcr.microsoft.com/dotnet/runtime:10.0
        WORKDIR /app
        COPY --from=build /app .
        ENTRYPOINT ["dotnet", "Server.dll"]
        """;
    }

    public static string RenderClusterCompose(NewCommandOptions options)
    {
        var endpointPath = string.Equals(options.Transport, "websocket", StringComparison.OrdinalIgnoreCase) ? "/ws" : "";
        var advertisedClientEndpoint = RenderAdvertisedClientEndpoint(options.Transport, "gateway", 20000, endpointPath);
        var healthCommand = "dotnet Server.dll --health-check";

        return $$"""
        services:
          gateway:
            build:
              context: .
              dockerfile: Server/Dockerfile
            environment:
              LakonaGame__Endpoints__0__Transport: "{{TemplateText.SanitizeStringLiteral(options.Transport)}}"
              LakonaGame__Endpoints__0__Host: "0.0.0.0"
              LakonaGame__Endpoints__0__Port: "20000"
              LakonaGame__Endpoints__0__Path: "{{TemplateText.SanitizeStringLiteral(endpointPath)}}"
              Cluster__NodeId: "${LAKONA_CLUSTER_NODE_ID:-gateway-1}"
              Cluster__AdvertisedEndpoints__cluster: "${LAKONA_CLUSTER_ADVERTISED_ENDPOINTS_CLUSTER:-tcp://gateway:21000}"
              Cluster__AdvertisedEndpoints__client: "${LAKONA_CLUSTER_ADVERTISED_ENDPOINTS_CLIENT:-{{TemplateText.SanitizeStringLiteral(advertisedClientEndpoint)}}}"
              Cluster__Bootstrap__NodeDirectoryEndpoints__0: "${LAKONA_CLUSTER_BOOTSTRAP_NODE_DIRECTORY_ENDPOINT_0:-tcp://gateway:21000}"
              Cluster__NodeDirectory__Enabled: "${LAKONA_CLUSTER_NODE_DIRECTORY_ENABLED:-true}"
              Cluster__NodeDirectory__Storage__Mode: "${LAKONA_CLUSTER_NODE_DIRECTORY_STORAGE_MODE:-InMemory}"
              Cluster__Services__0__Kind: "node-directory"
              Cluster__Services__0__Name: "node-directory"
              Cluster__Services__1__Kind: "route-directory"
              Cluster__Services__1__Name: "route-directory"
              Cluster__Services__2__Kind: "gateway"
              Cluster__Services__2__Name: "gateway"
              Cluster__RouteLeaseSeconds: "${LAKONA_CLUSTER_ROUTE_LEASE_SECONDS:-30}"
              Cluster__SendTimeoutMilliseconds: "${LAKONA_CLUSTER_SEND_TIMEOUT_MILLISECONDS:-2000}"
            ports:
              - "20000:20000"
            healthcheck:
              test: ["CMD-SHELL", "{{TemplateText.SanitizeStringLiteral(healthCommand)}}"]
              interval: 10s
              timeout: 3s
              retries: 3
              start_period: 10s
        """;
    }

    public static string RenderClusterEnvExample(NewCommandOptions options)
    {
        var endpointPath = string.Equals(options.Transport, "websocket", StringComparison.OrdinalIgnoreCase) ? "/ws" : "";
        var advertisedClientEndpoint = RenderAdvertisedClientEndpoint(options.Transport, "gateway", 20000, endpointPath);

        return $$"""
        # This file intentionally contains no production secrets.
        # Put node authentication and TLS material in your deployment platform secret store.
        LAKONA_CLUSTER_NODE_ID=gateway-1
        LAKONA_CLUSTER_ADVERTISED_ENDPOINTS_CLUSTER=tcp://gateway:21000
        LAKONA_CLUSTER_ADVERTISED_ENDPOINTS_CLIENT={{advertisedClientEndpoint}}
        LAKONA_CLUSTER_BOOTSTRAP_NODE_DIRECTORY_ENDPOINT_0=tcp://gateway:21000
        LAKONA_CLUSTER_NODE_DIRECTORY_ENABLED=true
        LAKONA_CLUSTER_NODE_DIRECTORY_STORAGE_MODE=InMemory
        LAKONA_CLUSTER_ROUTE_LEASE_SECONDS=30
        LAKONA_CLUSTER_SEND_TIMEOUT_MILLISECONDS=2000
        """;
    }

    public static string RenderClusterOperationsGuide()
    {
        return """
        # Cluster Operations

        This scaffold is an opt-in starting point for local cluster deployment rehearsal.

        It intentionally does not define production secrets. Node authentication keys, TLS certificates, database credentials, and deployment tokens must come from the deployment platform secret store or a project-owned secret management flow.

        Generated cluster settings can be overridden with environment variables:

        - `Cluster__NodeId`
        - `Cluster__AdvertisedEndpoints__cluster`
        - `Cluster__AdvertisedEndpoints__client`
        - `Cluster__Bootstrap__NodeDirectoryEndpoints__0`
        - `Cluster__NodeDirectory__Enabled`
        - `Cluster__NodeDirectory__Storage__Mode`
        - `Cluster__Services__0__Kind`
        - `Cluster__Services__0__Name`
        - `Cluster__RouteLeaseSeconds`
        - `Cluster__SendTimeoutMilliseconds`

        Health check:

        ```bash
        dotnet Server.dll --health-check
        ```

        The generated health check validates that local cluster configuration has a node id, at least one advertised endpoint, and at least one configured service. Remote node-directory, route-directory, and node-messenger dependency checks should be wired by the project host using `ClusterDependencyProbe` once the project chooses its concrete topology and secret policy.
        """;
    }

    private static string RenderAdvertisedClientEndpoint(
        string transport,
        string host,
        int port,
        string path)
    {
        var scheme = transport switch
        {
            "websocket" => "ws",
            "tcp" => "tcp",
            _ => "kcp"
        };
        return string.IsNullOrWhiteSpace(path)
            ? $"{scheme}://{host}:{port}"
            : $"{scheme}://{host}:{port}{path}";
    }
}
