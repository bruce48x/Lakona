using Lakona.Game.Cluster.Rpc.Membership;

namespace Lakona.Game.Server.Configuration;

/// <summary>
/// Configures low-level cluster runtime services derived from game runtime configuration.
/// </summary>
public sealed class ClusterOptions
{
    /// <summary>
    /// Gets the node id used by cluster membership and route ownership.
    /// </summary>
    public string NodeId { get; init; } = "gateway-1";

    /// <summary>
    /// Gets advertised endpoint URIs keyed by endpoint role or transport.
    /// </summary>
    public IReadOnlyDictionary<string, string> AdvertisedEndpoints { get; init; } =
        new Dictionary<string, string>
        {
            ["cluster"] = "tcp://127.0.0.1:21000"
        };

    /// <summary>
    /// Gets the cluster send timeout in milliseconds.
    /// </summary>
    public int SendTimeoutMilliseconds { get; init; } =
        ClusterMembershipNodeOptions.DefaultRequestTimeoutMilliseconds;
}
