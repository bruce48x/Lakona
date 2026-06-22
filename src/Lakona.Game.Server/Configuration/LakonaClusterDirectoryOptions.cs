namespace Lakona.Game.Server.Configuration;

public sealed class LakonaClusterDirectoryOptions
{
    public string Provider { get; init; } = "";

    public string ConnectionStringName { get; init; } = "";

    public string NodeTable { get; init; } = "lakona_cluster_nodes";

    public bool EnsureSchemaOnStartup { get; init; }
}
