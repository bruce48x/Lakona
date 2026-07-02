namespace Lakona.Game.Server.Configuration;

/// <summary>
/// Configures storage for the framework-owned cluster node directory.
/// </summary>
public sealed class LakonaClusterDirectoryOptions
{
    /// <summary>
    /// Gets the node-directory provider name, such as <c>postgres</c>.
    /// </summary>
    public string Provider { get; init; } = "";

    /// <summary>
    /// Gets the connection-string name used by the selected node-directory provider.
    /// </summary>
    public string ConnectionStringName { get; init; } = "";

    /// <summary>
    /// Gets the database table name used for cluster node membership.
    /// </summary>
    public string NodeTable { get; init; } = "lakona_cluster_nodes";

    /// <summary>
    /// Gets a value indicating whether startup may create the directory schema.
    /// </summary>
    /// <remarks>
    /// Production deployments should normally apply schema through controlled
    /// migration tooling and leave this disabled.
    /// </remarks>
    public bool EnsureSchemaOnStartup { get; init; }
}
