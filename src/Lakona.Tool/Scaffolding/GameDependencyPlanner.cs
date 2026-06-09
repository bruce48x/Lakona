internal sealed record GamePackageReference(
    string Id,
    string Version,
    string? PrivateAssets = null,
    string? OutputItemType = null,
    string? IncludeAssets = null);

internal sealed record GameDependencyPlan(IReadOnlyList<GamePackageReference> PackageReferences);

internal static class GameDependencyPlanner
{
    private const string RpcCoreVersion = "0.12.0";
    private const string RpcServerVersion = "0.12.1";
    private const string RpcTransportTcpVersion = "0.11.4";
    private const string RpcTransportWebSocketVersion = "0.11.6";
    private const string RpcTransportKcpVersion = "0.11.13";
    private const string RpcSerializerJsonVersion = "0.11.1";
    private const string RpcSerializerMemoryPackVersion = "0.11.1";
    private const string RpcAnalyzersVersion = "0.2.0";

    public static GameDependencyPlan CreateServerPlan(NewCommandOptions options)
    {
        var references = new List<GamePackageReference>
        {
            new("Microsoft.Extensions.Hosting", ToolPackageVersions.MicrosoftExtensionsHosting),
            new("Lakona.Game.Server", ToolPackageVersions.LakonaGameServer),
            new("Lakona.Game.Server.Generators", ToolPackageVersions.LakonaGameServerGenerators, PrivateAssets: "all", OutputItemType: "Analyzer"),
            new("Lakona.Game.Server.Hotfix", ToolPackageVersions.LakonaGameServerHotfix),
            new("Lakona.Game.Server.Hotfix.Generators", ToolPackageVersions.LakonaGameServerHotfixGenerators, PrivateAssets: "all", OutputItemType: "Analyzer"),
            new("Lakona.Rpc.Server", RpcServerVersion),
            new(GetTransportPackage(options.Transport), GetTransportVersion(options.Transport)),
            new("Lakona.Rpc.Analyzers", RpcAnalyzersVersion,
                PrivateAssets: "all",
                OutputItemType: null,
                IncludeAssets: "runtime; build; native; contentfiles; analyzers; buildtransitive")
        };

        if (options.Serializer == "json")
        {
            references.Add(new("Lakona.Rpc.Serializer.Json", RpcSerializerJsonVersion));
        }

        if (ProjectConventions.IsClusterNetworkProfile(options.NetworkProfile))
        {
            references.Add(new("Lakona.Game.Cluster", ToolPackageVersions.LakonaGameCluster));
            references.Add(new("Lakona.Game.Cluster.Rpc", ToolPackageVersions.LakonaGameClusterRpc));
        }

        if (ProjectConventions.UsesExternalPersistence(options.Persistence))
        {
            references.Add(new("Dapper", ToolPackageVersions.Dapper));
            references.Add(string.Equals(options.Persistence, "mysql", StringComparison.OrdinalIgnoreCase)
                ? new GamePackageReference("MySqlConnector", ToolPackageVersions.MySqlConnector)
                : new GamePackageReference("Npgsql", ToolPackageVersions.Npgsql));
        }

        return new GameDependencyPlan(references);
    }

    private static string GetTransportPackage(string transport) => transport switch
    {
        "tcp" => "Lakona.Rpc.Transport.Tcp",
        "websocket" => "Lakona.Rpc.Transport.WebSocket",
        _ => "Lakona.Rpc.Transport.Kcp"
    };

    private static string GetTransportVersion(string transport) => transport switch
    {
        "tcp" => RpcTransportTcpVersion,
        "websocket" => RpcTransportWebSocketVersion,
        _ => RpcTransportKcpVersion
    };
}
