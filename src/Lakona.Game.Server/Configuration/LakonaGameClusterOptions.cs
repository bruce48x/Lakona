namespace Lakona.Game.Server.Configuration;

/// <summary>
/// Configures node-to-node cluster infrastructure under <c>Lakona:Cluster</c>.
/// </summary>
public sealed class LakonaGameClusterOptions
{
    /// <summary>
    /// Gets the local cluster endpoint advertised for node-to-node traffic.
    /// </summary>
    public string Endpoint { get; init; } = "";

    /// <summary>
    /// Gets the serializer used for cluster protocol, feature messages, notification relay, and remote actor payloads.
    /// </summary>
    public string Serializer { get; init; } = "";

    /// <summary>
    /// Gets bootstrap cluster endpoints used to reach shared directory services.
    /// </summary>
    public IReadOnlyList<string> Seeds { get; init; } = [];

    /// <summary>
    /// Gets cluster node-directory storage settings.
    /// </summary>
    public LakonaClusterDirectoryOptions Directory { get; init; } = new();
}
