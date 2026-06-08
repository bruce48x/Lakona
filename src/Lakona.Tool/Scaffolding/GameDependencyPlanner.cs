internal sealed record GamePackageReference(
    string Id,
    string Version,
    string? PrivateAssets = null,
    string? OutputItemType = null);

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
            new("Lakona.Game.Server.Hotfix", ToolPackageVersions.LakonaGameServerHotfix)
        };

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
}
