internal sealed record GamePackageReference(
    string Id,
    string Version,
    string? PrivateAssets = null,
    string? OutputItemType = null,
    string? IncludeAssets = null);

internal sealed record GameDependencyPlan(IReadOnlyList<GamePackageReference> PackageReferences);

internal static class GameDependencyPlanner
{
    public static GameDependencyPlan CreateServerPlan(NewCommandOptions options)
    {
        var references = new List<GamePackageReference>
        {
            new("Microsoft.Extensions.Hosting", ToolPackageVersions.MicrosoftExtensionsHosting),
            new("Lakona.Game.Server", ToolPackageVersions.LakonaGameServer),
            new("Lakona.Game.Server.Generators", ToolPackageVersions.LakonaGameServerGenerators, PrivateAssets: "all", OutputItemType: "Analyzer"),
            new("Lakona.Game.Server.Hotfix", ToolPackageVersions.LakonaGameServerHotfix),
            new("Lakona.Game.Server.Hotfix.Generators", ToolPackageVersions.LakonaGameServerHotfixGenerators, PrivateAssets: "all", OutputItemType: "Analyzer"),
            new("Lakona.Rpc.Server", ToolPackageVersions.LakonaRpcServer),
            new(GetTransportPackage(options.Transport), GetTransportVersion(options.Transport)),
            new("Lakona.Rpc.Analyzers", ToolPackageVersions.LakonaRpcAnalyzers,
                PrivateAssets: "all",
                OutputItemType: null,
                IncludeAssets: "runtime; build; native; contentfiles; analyzers; buildtransitive")
        };

        if (options.Serializer == "json")
        {
            references.Add(new("Lakona.Rpc.Serializer.Json", ToolPackageVersions.LakonaRpcSerializerJson));
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
        "tcp" => ToolPackageVersions.LakonaRpcTransportTcp,
        "websocket" => ToolPackageVersions.LakonaRpcTransportWebSocket,
        _ => ToolPackageVersions.LakonaRpcTransportKcp
    };
}
