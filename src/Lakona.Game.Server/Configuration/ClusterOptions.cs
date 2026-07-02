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
    /// Gets node-directory behavior and storage settings.
    /// </summary>
    public ClusterNodeDirectoryOptions NodeDirectory { get; init; } = new();

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

/// <summary>
/// Configures cluster node-directory behavior.
/// </summary>
public sealed class ClusterNodeDirectoryOptions
{
    /// <summary>
    /// Gets a value indicating whether node-directory services are enabled.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets node-directory storage settings.
    /// </summary>
    public ClusterNodeDirectoryStorageOptions Storage { get; init; } = new();

    /// <summary>
    /// Binds node-directory options from configuration.
    /// </summary>
    /// <param name="section">The node-directory configuration section.</param>
    /// <param name="defaults">The defaults to use when settings are omitted.</param>
    /// <returns>The bound node-directory options.</returns>
    public static ClusterNodeDirectoryOptions FromConfiguration(
        IConfigurationSection section,
        ClusterNodeDirectoryOptions defaults)
    {
        return new ClusterNodeDirectoryOptions
        {
            Enabled = LakonaConfigurationReader.ReadBool(section, "Enabled", defaults.Enabled),
            Storage = ClusterNodeDirectoryStorageOptions.FromConfiguration(section.GetSection("Storage"), defaults.Storage)
        };
    }
}

/// <summary>
/// Configures storage for the cluster node directory.
/// </summary>
public sealed class ClusterNodeDirectoryStorageOptions
{
    /// <summary>
    /// Gets the storage mode, such as <c>InMemory</c> or provider-backed storage.
    /// </summary>
    public string Mode { get; init; } = "InMemory";

    /// <summary>
    /// Gets the storage provider name.
    /// </summary>
    public string Provider { get; init; } = "";

    /// <summary>
    /// Gets the connection-string name used by the selected provider.
    /// </summary>
    public string ConnectionStringName { get; init; } = "";

    /// <summary>
    /// Binds node-directory storage options from configuration.
    /// </summary>
    /// <param name="section">The storage configuration section.</param>
    /// <param name="defaults">The defaults to use when settings are omitted.</param>
    /// <returns>The bound storage options.</returns>
    public static ClusterNodeDirectoryStorageOptions FromConfiguration(
        IConfigurationSection section,
        ClusterNodeDirectoryStorageOptions defaults)
    {
        return new ClusterNodeDirectoryStorageOptions
        {
            Mode = LakonaConfigurationReader.ReadString(section, "Mode", defaults.Mode),
            Provider = LakonaConfigurationReader.ReadString(section, "Provider", defaults.Provider),
            ConnectionStringName = LakonaConfigurationReader.ReadString(section, "ConnectionStringName", defaults.ConnectionStringName)
        };
    }
}
