using Microsoft.Extensions.Configuration;

namespace Lakona.Game.Server.Configuration;

public sealed class ClusterOptions
{
    public string NodeId { get; init; } = "gateway-1";
    public IReadOnlyDictionary<string, string> AdvertisedEndpoints { get; init; } =
        new Dictionary<string, string>
        {
            ["cluster"] = "tcp://127.0.0.1:21000",
            ["client"] = "tcp://127.0.0.1:20000"
        };
    public ClusterBootstrapOptions Bootstrap { get; init; } = new();
    public ClusterNodeDirectoryOptions NodeDirectory { get; init; } = new();
    public IReadOnlyList<ClusterServiceOptions> Services { get; init; } =
        new[]
        {
            new ClusterServiceOptions { Kind = "node-directory", Name = "node-directory" },
            new ClusterServiceOptions { Kind = "route-directory", Name = "route-directory" },
            new ClusterServiceOptions { Kind = "gateway", Name = "gateway" }
        };
    public int RouteLeaseSeconds { get; init; } = 30;
    public int SendTimeoutMilliseconds { get; init; } = 2000;
}

public sealed class ClusterBootstrapOptions
{
    public IReadOnlyList<string> NodeDirectoryEndpoints { get; init; } =
        new[] { "tcp://127.0.0.1:21000" };

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

public sealed class ClusterNodeDirectoryOptions
{
    public bool Enabled { get; init; } = true;
    public ClusterNodeDirectoryStorageOptions Storage { get; init; } = new();

    public static ClusterNodeDirectoryOptions FromConfiguration(
        IConfigurationSection section,
        ClusterNodeDirectoryOptions defaults)
    {
        return new ClusterNodeDirectoryOptions
        {
            Enabled = ReadBool(section, "Enabled", defaults.Enabled),
            Storage = ClusterNodeDirectoryStorageOptions.FromConfiguration(section.GetSection("Storage"), defaults.Storage)
        };
    }

    private static bool ReadBool(IConfiguration section, string name, bool fallback)
    {
        return bool.TryParse(section[name], out var value) ? value : fallback;
    }
}

public sealed class ClusterNodeDirectoryStorageOptions
{
    public string Mode { get; init; } = "InMemory";
    public string Provider { get; init; } = "";
    public string ConnectionStringName { get; init; } = "";

    public static ClusterNodeDirectoryStorageOptions FromConfiguration(
        IConfigurationSection section,
        ClusterNodeDirectoryStorageOptions defaults)
    {
        return new ClusterNodeDirectoryStorageOptions
        {
            Mode = ReadString(section, "Mode", defaults.Mode),
            Provider = ReadString(section, "Provider", defaults.Provider),
            ConnectionStringName = ReadString(section, "ConnectionStringName", defaults.ConnectionStringName)
        };
    }

    private static string ReadString(IConfiguration section, string name, string fallback)
    {
        var value = section[name];
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}

public sealed class ClusterServiceOptions
{
    public string Kind { get; init; } = "";
    public string Name { get; init; } = "";
}
