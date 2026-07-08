using System.Text.Json;
using Lakona.Game.Server.Observability;
using Microsoft.Extensions.Configuration;

namespace Lakona.Game.Server.Configuration;

/// <summary>
/// Represents the runtime configuration bound from the <c>Lakona</c> configuration root.
/// </summary>
/// <remarks>
/// These options describe the node identity, client-facing endpoints, feature
/// selection, cluster endpoint, and observability settings for one server
/// process. <see cref="Lakona.Game.Server.Hosting.LakonaGameServer"/> binds
/// this type during startup.
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
    /// Gets actor kinds this node may host.
    /// </summary>
    public IReadOnlyList<string> ActorHosts { get; init; } = [];

    /// <summary>
    /// Gets named startup actor declarations activated on this node.
    /// </summary>
    public IReadOnlyList<LakonaGameStartupActorOptions> StartupActors { get; init; } = [];

    /// <summary>
    /// Gets node-to-node cluster configuration.
    /// </summary>
    public LakonaGameClusterOptions Cluster { get; init; } = LakonaGameClusterOptions.Defaults();

    /// <summary>
    /// Gets logging, diagnostics, metrics, tracing, and local-admin settings.
    /// </summary>
    public LakonaObservabilityOptions Observability { get; init; } =
        LakonaObservabilityOptions.Defaults();

    /// <summary>
    /// Gets server-owned game heartbeat timing policy.
    /// </summary>
    public LakonaGameHeartbeatOptions Heartbeat { get; init; } = LakonaGameHeartbeatOptions.Defaults();

    /// <summary>
    /// Binds runtime options from the <c>Lakona</c> configuration root.
    /// </summary>
    /// <param name="configuration">The host configuration to read.</param>
    /// <returns>The bound runtime options.</returns>
    public static LakonaGameRuntimeOptions FromConfiguration(IConfiguration configuration)
    {
        var section = GetRuntimeSection(configuration);

        return new LakonaGameRuntimeOptions
        {
            Node = BindNode(section.GetSection("Node")),
            Endpoints = BindEndpoints(section.GetSection("Endpoints")),
            Feature = BindOptionalStringArray(section.GetSection("Feature")),
            ActorHosts = BindStringArray(section.GetSection("ActorHosts")),
            StartupActors = BindStartupActors(section.GetSection("StartupActors")),
            Cluster = BindCluster(section.GetSection("Cluster")),
            Heartbeat = LakonaGameHeartbeatOptions.FromConfiguration(section.GetSection("Heartbeat")),
            Observability = LakonaObservabilityOptions.FromConfiguration(configuration)
        };
    }

    /// <summary>
    /// Converts game runtime options into cluster runtime options.
    /// </summary>
    /// <returns>The cluster options derived from the current node and endpoint configuration.</returns>
    public ClusterOptions ToClusterOptions()
    {
        var advertisedEndpoints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(Cluster.Endpoint))
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
                NodeDirectoryEndpoints = Cluster.Seeds.Count > 0
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

    private static LakonaGameClusterOptions BindCluster(IConfigurationSection section)
    {
        if (!section.GetChildren().Any())
        {
            if (section.Value is not null)
            {
                return new LakonaGameClusterOptions
                {
                    Endpoint = section.Value,
                    Serializer = ""
                };
            }

            return LakonaGameClusterOptions.Defaults();
        }

        return new LakonaGameClusterOptions
        {
            Endpoint = ReadClusterString(section, "Endpoint", LakonaGameClusterOptions.DefaultEndpoint),
            Serializer = ReadClusterString(section, "Serializer", LakonaGameClusterOptions.DefaultSerializer),
            Seeds = BindStringArray(section.GetSection("Seeds")),
            Directory = BindClusterDirectory(section.GetSection("Directory"))
        };
    }

    private static string ReadClusterString(IConfiguration section, string name, string fallback)
    {
        return section[name] ?? fallback;
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

    private static IReadOnlyList<LakonaGameStartupActorOptions> BindStartupActors(IConfigurationSection section)
    {
        if (TryReadJsonValue(section, out var json))
        {
            return ParseStartupActorJsonArray(section.Path, json);
        }

        return section
            .GetChildren()
            .Select(BindStartupActor)
            .ToArray();
    }

    private static LakonaGameStartupActorOptions BindStartupActor(IConfigurationSection section)
    {
        if (!section.GetChildren().Any())
        {
            return new LakonaGameStartupActorOptions
            {
                Name = section.Value ?? ""
            };
        }

        return new LakonaGameStartupActorOptions
        {
            Name = section["Name"] ?? section.Value ?? "",
            Options = LakonaConfigurationReader.ReadDictionary(
                section.GetSection("Options"),
                new Dictionary<string, string>(StringComparer.Ordinal))
        };
    }

    private static IReadOnlyList<LakonaGameStartupActorOptions> ParseStartupActorJsonArray(
        string path,
        string json)
    {
        try
        {
            var elements = JsonSerializer.Deserialize<JsonElement[]>(json, JsonOptions) ?? [];
            return elements.Select((element, index) => ParseStartupActorJsonElement(path, index, element)).ToArray();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"{path} must be a valid JSON array when configured as a string value.",
                ex);
        }
    }

    private static LakonaGameStartupActorOptions ParseStartupActorJsonElement(
        string path,
        int index,
        JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return new LakonaGameStartupActorOptions
            {
                Name = element.GetString() ?? ""
            };
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"{path}:{index} must be a startup actor name or object.");
        }

        var name = element.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
            ? nameElement.GetString() ?? ""
            : "";
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        if (element.TryGetProperty("options", out var optionsElement)
            && optionsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in optionsElement.EnumerateObject())
            {
                options[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? ""
                    : property.Value.ToString();
            }
        }

        return new LakonaGameStartupActorOptions
        {
            Name = name,
            Options = options
        };
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

public sealed class LakonaGameStartupActorOptions
{
    public string Name { get; init; } = "";

    public IReadOnlyDictionary<string, string> Options { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
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

public sealed class LakonaGameHeartbeatOptions
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(45);

    public static LakonaGameHeartbeatOptions Defaults()
    {
        return new LakonaGameHeartbeatOptions();
    }

    public static LakonaGameHeartbeatOptions FromConfiguration(IConfigurationSection section)
    {
        return new LakonaGameHeartbeatOptions
        {
            Interval = ReadTimeSpan(section, "Interval", TimeSpan.FromSeconds(15)),
            Timeout = ReadTimeSpan(section, "Timeout", TimeSpan.FromSeconds(45))
        };
    }

    private static TimeSpan ReadTimeSpan(IConfigurationSection section, string key, TimeSpan fallback)
    {
        var value = section[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (TimeSpan.TryParse(value, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"{section.Path}:{key} must be a TimeSpan value such as 00:00:15.");
    }
}
