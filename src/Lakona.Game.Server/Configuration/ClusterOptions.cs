using Microsoft.Extensions.Configuration;

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
    /// Gets bootstrap settings used to reach cluster directory services.
    /// </summary>
    public ClusterBootstrapOptions Bootstrap { get; init; } = new();

    /// <summary>
    /// Gets the route lease duration in seconds.
    /// </summary>
    public int RouteLeaseSeconds { get; init; } = 30;

    /// <summary>
    /// Gets the cluster send timeout in milliseconds.
    /// </summary>
    public int SendTimeoutMilliseconds { get; init; } = 2000;
}

/// <summary>
/// Configures bootstrap access to shared cluster directory endpoints.
/// </summary>
public sealed class ClusterBootstrapOptions
{
    /// <summary>
    /// Gets cluster endpoints that can serve node-directory requests.
    /// </summary>
    public IReadOnlyList<string> NodeDirectoryEndpoints { get; init; } =
        new[] { "tcp://127.0.0.1:21000" };

    /// <summary>
    /// Binds bootstrap options from configuration.
    /// </summary>
    /// <param name="section">The bootstrap configuration section.</param>
    /// <param name="defaults">The defaults to use when settings are omitted.</param>
    /// <returns>The bound bootstrap options.</returns>
    public static ClusterBootstrapOptions FromConfiguration(
        IConfigurationSection section,
        ClusterBootstrapOptions defaults)
    {
        return new ClusterBootstrapOptions
        {
            NodeDirectoryEndpoints = ReadList(section.GetSection("NodeDirectoryEndpoints"), defaults.NodeDirectoryEndpoints)
        };
    }

    private static IReadOnlyList<string> ReadList(
        IConfigurationSection section,
        IReadOnlyList<string> fallback)
    {
        var values = new List<string>();
        foreach (var child in section.GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(child.Value))
            {
                values.Add(child.Value!);
            }
        }
        return values.Count == 0 ? fallback : values;
    }
}
