using System.Text.Json;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Observability;
using Microsoft.Extensions.Configuration;

namespace Lakona.Game.Server.Configuration;

/// <summary>
/// Represents the runtime configuration bound from the <c>Lakona</c> configuration root.
/// </summary>
/// <remarks>
/// These options describe the node identity, client-facing endpoints, feature
/// selection, cluster endpoint, runtime profile, and observability settings for
/// one server process. <see cref="LakonaGameServer"/> binds this type during
/// startup.
/// </remarks>
public sealed class LakonaGameRuntimeOptions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Gets the identity of the current server process.
    /// </summary>
    public LakonaGameNodeOptions Node { get; init; } = new();

    /// <summary>
    /// Gets the client-facing RPC listener configuration.
    /// </summary>
    public IReadOnlyList<LakonaGameEndpointOptions> Endpoints { get; init; } = [];

    /// <summary>
    /// Gets the active stable feature names, or <see langword="null"/> when all discovered features are active.
    /// </summary>
    /// <remarks>
    /// An empty list is meaningful: it disables all application features while
    /// still allowing the process to expose RPC endpoints or framework services.
    /// </remarks>
    public IReadOnlyList<string>? Feature { get; init; }

    /// <summary>
    /// Gets node-to-node cluster configuration, or <see langword="null"/> for a single-node process.
    /// </summary>
    public LakonaGameClusterOptions? Cluster { get; init; }

    /// <summary>
    /// Gets the resolved runtime profile that controls framework defaults and guardrails.
    /// </summary>
    public LakonaGameRuntimeProfile Profile { get; init; } = LakonaGameRuntimeProfile.Development;

    /// <summary>
    /// Gets logging, diagnostics, metrics, tracing, and local-admin settings.
    /// </summary>
    public LakonaObservabilityOptions Observability { get; init; } =
        LakonaObservabilityOptions.Defaults(LakonaGameRuntimeProfile.Development);

    /// <summary>
    /// Binds runtime options from the <c>Lakona</c> configuration root.
    /// </summary>
    /// <param name="configuration">The host configuration to read.</param>
    /// <param name="environmentName">The optional host environment name used to resolve the runtime profile.</param>
    /// <returns>The bound runtime options.</returns>
    public static LakonaGameRuntimeOptions FromConfiguration(
        IConfiguration configuration,
        string? environmentName = null)
    {
        var section = GetRuntimeSection(configuration);
        var profile = LakonaGameRuntimeProfileResolver.Resolve(configuration, environmentName);

        return new LakonaGameRuntimeOptions
        {
            Node = BindNode(section.GetSection("Node")),
            Endpoints = BindEndpoints(section.GetSection("Endpoints")),
            Feature = BindOptionalStringArray(section.GetSection("Feature")),
            Cluster = BindCluster(section.GetSection("Cluster")),
            Profile = profile,
            Observability = LakonaObservabilityOptions.FromConfiguration(configuration, profile)
        };
    }

    /// <summary>
    /// Converts game runtime options into cluster runtime options.
    /// </summary>
    /// <returns>The cluster options derived from the current node and endpoint configuration.</returns>
    public ClusterOptions ToClusterOptions()
    {
        var advertisedEndpoints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (Cluster is not null && !string.IsNullOrWhiteSpace(Cluster.Endpoint))
        {
            advertisedEndpoints["cluster"] = Cluster.Endpoint;
        }

        foreach (var endpoint in Endpoints)
        {
            var transport = endpoint.Transport.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(transport))
            {
                continue;
            }

            if (!advertisedEndpoints.TryAdd(transport, endpoint.ToAdvertisedEndpoint()))
            {
                throw new InvalidOperationException(
                    $"Endpoint transport '{endpoint.Transport}' is configured more than once.");
            }
        }

        return new ClusterOptions
        {
            NodeId = Node.Id,
            AdvertisedEndpoints = advertisedEndpoints,
            Bootstrap = new ClusterBootstrapOptions
            {
                NodeDirectoryEndpoints = Cluster?.Seeds.Count > 0
                    ? Cluster.Seeds
                    : new ClusterBootstrapOptions().NodeDirectoryEndpoints
            }
        };
    }

    /// <summary>
    /// Converts game runtime options into cluster runtime options, applying cluster-specific overrides.
    /// </summary>
    /// <param name="configuration">The host configuration that may contain <c>Lakona:Cluster</c> overrides.</param>
    /// <returns>The cluster options used by node-to-node framework services.</returns>
    public ClusterOptions ToClusterOptions(IConfiguration configuration)
    {
        var defaults = ToClusterOptions();
        var section = GetRuntimeSection(configuration).GetSection("Cluster");

        return new ClusterOptions
        {
            NodeId = LakonaConfigurationReader.ReadString(section, "NodeId", defaults.NodeId),
            AdvertisedEndpoints = LakonaConfigurationReader.ReadDictionary(
                section.GetSection("AdvertisedEndpoints"), defaults.AdvertisedEndpoints),
            Bootstrap = ClusterBootstrapOptions.FromConfiguration(
                section.GetSection("Bootstrap"), defaults.Bootstrap),
            NodeDirectory = ClusterNodeDirectoryOptions.FromConfiguration(
                section.GetSection("NodeDirectory"), defaults.NodeDirectory),
            RouteLeaseSeconds = LakonaConfigurationReader.ReadInt(section, "RouteLeaseSeconds", defaults.RouteLeaseSeconds),
            SendTimeoutMilliseconds = LakonaConfigurationReader.ReadInt(section, "SendTimeoutMilliseconds", defaults.SendTimeoutMilliseconds)
        };
    }

    private static IConfigurationSection GetRuntimeSection(IConfiguration configuration)
    {
        return configuration.GetSection("Lakona");
    }

    private static LakonaGameNodeOptions BindNode(IConfiguration section)
    {
        return LakonaGameNodeOptions.FromConfiguration(section);
    }

    private static IReadOnlyList<LakonaGameEndpointOptions> BindEndpoints(IConfigurationSection section)
    {
        if (TryReadJsonValue(section, out var json))
        {
            return ParseJsonArray<LakonaGameEndpointOptions>(section.Path, json);
        }

        return section
            .GetChildren()
            .Select(endpoint => new LakonaGameEndpointOptions
            {
                Transport = endpoint["Transport"] ?? "",
                Serializer = endpoint["Serializer"] ?? "",
                Host = endpoint["Host"] ?? "",
                Port = LakonaConfigurationReader.ReadInt(endpoint["Port"]),
                Path = endpoint["Path"] ?? "",
                AdvertisedHost = endpoint["AdvertisedHost"] ?? "",
                RpcServices = BindStringArray(endpoint.GetSection("RpcServices"))
            })
            .ToArray();
    }

    private static IReadOnlyList<string>? BindOptionalStringArray(IConfigurationSection section)
    {
        var values = section
            .GetChildren()
            .Select(child => child.Value ?? "")
            .ToArray();

        if (values.Length > 0)
        {
            return values;
        }

        if (TryReadJsonValue(section, out var json))
        {
            return ParseJsonArray<string>(section.Path, json);
        }

        return section.Value is null ? null : Array.Empty<string>();
    }

    private static LakonaGameClusterOptions? BindCluster(IConfiguration section)
    {
        if (!section.GetChildren().Any())
        {
            return null;
        }

        return new LakonaGameClusterOptions
        {
            Endpoint = section["Endpoint"] ?? "",
            Serializer = section["Serializer"] ?? "",
            Seeds = BindStringArray(section.GetSection("Seeds")),
            Directory = BindClusterDirectory(section.GetSection("Directory"))
        };
    }

    private static LakonaClusterDirectoryOptions BindClusterDirectory(IConfiguration section)
    {
        return new LakonaClusterDirectoryOptions
        {
            Provider = section["Provider"] ?? "",
            ConnectionStringName = section["ConnectionStringName"] ?? "",
            NodeTable = LakonaConfigurationReader.ReadString(section, "NodeTable", "lakona_cluster_nodes"),
            EnsureSchemaOnStartup = bool.TryParse(section["EnsureSchemaOnStartup"], out var parsed) && parsed
        };
    }

    private static IReadOnlyList<string> BindStringArray(IConfigurationSection section)
    {
        if (TryReadJsonValue(section, out var json))
        {
            return ParseJsonArray<string>(section.Path, json);
        }

        return section
            .GetChildren()
            .Select(child => child.Value ?? "")
            .ToArray();
    }

    private static bool TryReadJsonValue(IConfigurationSection section, out string json)
    {
        var value = section.Value;
        if (!string.IsNullOrWhiteSpace(value)
            && value.TrimStart().StartsWith("[", StringComparison.Ordinal))
        {
            json = value;
            return true;
        }

        json = "";
        return false;
    }

    private static IReadOnlyList<T> ParseJsonArray<T>(string path, string json)
    {
        try
        {
            var values = JsonSerializer.Deserialize<T[]>(json, JsonOptions);
            return values ?? [];
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"{path} must be a valid JSON array when configured as a string value.",
                ex);
        }
    }

}

/// <summary>
/// Configures the stable identity of one Lakona server process.
/// </summary>
public sealed class LakonaGameNodeOptions
{
    /// <summary>
    /// Gets the node id used for cluster membership, diagnostics, and route ownership.
    /// </summary>
    public string Id { get; init; } = "dev-1";

    /// <summary>
    /// Binds node options from a <c>Lakona:Node</c> configuration section.
    /// </summary>
    /// <param name="section">The node configuration section.</param>
    /// <returns>The bound node options.</returns>
    public static LakonaGameNodeOptions FromConfiguration(IConfiguration section)
    {
        return new LakonaGameNodeOptions
        {
            Id = LakonaConfigurationReader.ReadString(section, "Id", "dev-1")
        };
    }
}
