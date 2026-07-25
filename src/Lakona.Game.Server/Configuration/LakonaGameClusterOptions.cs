namespace Lakona.Game.Server.Configuration;

/// <summary>
/// Configures node-to-node cluster infrastructure under <c>Lakona:Cluster</c>.
/// </summary>
public sealed class LakonaGameClusterOptions
{
    public const string DefaultEndpoint = "tcp://127.0.0.1:21001";

    /// <summary>
    /// Gets the local cluster endpoint advertised for node-to-node traffic.
    /// </summary>
    public string Endpoint { get; init; } = DefaultEndpoint;

    /// <summary>
    /// Gets whether this process is explicitly authorized to create a fresh cluster incarnation.
    /// </summary>
    public bool BootstrapNewCluster { get; init; }

    /// <summary>
    /// Gets bootstrap cluster endpoints used to join replicated membership.
    /// </summary>
    public IReadOnlyList<string> Seeds { get; init; } = [];

    public static LakonaGameClusterOptions Defaults()
    {
        return new LakonaGameClusterOptions();
    }
}
