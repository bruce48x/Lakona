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
    /// Gets the peer discovery hints used to find or form one cluster.
    /// </summary>
    public IReadOnlyList<LakonaGameClusterPeerOptions> Peers { get; init; } = [];

    public static LakonaGameClusterOptions Defaults()
    {
        return new LakonaGameClusterOptions();
    }
}

/// <summary>
/// Identifies one stable peer discovery hint.
/// </summary>
public sealed class LakonaGameClusterPeerOptions
{
    /// <summary>
    /// Gets the peer's stable node identity.
    /// </summary>
    public string Id { get; init; } = "";

    /// <summary>
    /// Gets the peer's advertised node-to-node endpoint.
    /// </summary>
    public string Endpoint { get; init; } = "";
}
