using System.Text.Json;
using System.Text.Json.Serialization;
using Lakona.Game.Server.Observability;
using Lakona.Game.Server.ReliablePush;
using Microsoft.Extensions.Configuration;
using Lakona.Game.Server.Sessions;

namespace Lakona.Game.Server.Configuration;

/// <summary>
/// Represents the runtime configuration bound from the <c>Lakona</c> configuration root.
/// </summary>
/// <remarks>
/// These options describe the node identity, client-facing endpoints, actor
/// hosting, cluster endpoint, and observability settings for one server process.
/// <see cref="Lakona.Game.Server.Hosting.LakonaGameServer"/> binds this type
/// during startup.
/// </remarks>
public sealed class LakonaGameRuntimeOptions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
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
    /// Gets application HTTP listener configuration.
    /// </summary>
    public LakonaHttpOptions Http { get; init; } = LakonaHttpOptions.Defaults();

    /// <summary>
    /// Gets actor kinds this node may host.
    /// </summary>
    public IReadOnlyList<string> ActorHosts { get; init; } = [];

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
    /// Gets process health endpoint settings.
    /// </summary>
    public LakonaHealthOptions Health { get; init; } = LakonaHealthOptions.Defaults();

    /// <summary>
    /// Gets the shared management-plane listener settings used by health and local-admin routes.
    /// </summary>
    public LakonaManagementOptions Management { get; init; } = LakonaManagementOptions.Defaults();

    /// <summary>
    /// Gets server-owned game heartbeat timing policy.
    /// </summary>
    public LakonaGameHeartbeatOptions Heartbeat { get; init; } = LakonaGameHeartbeatOptions.Defaults();

    public LakonaSessionHostingOptions Sessions { get; init; } = new();

    public ReliablePushOptions ReliablePush { get; init; } = new();

    public LakonaNotificationOptions Notifications { get; init; } = new();

    /// <summary>
    /// Binds runtime options from the <c>Lakona</c> configuration root.
    /// </summary>
    /// <param name="configuration">The host configuration to read.</param>
    /// <returns>The bound runtime options.</returns>
    public static LakonaGameRuntimeOptions FromConfiguration(IConfiguration configuration)
    {
        var section = GetRuntimeSection(configuration);
        if (section.GetSection("StartupActors").Exists())
        {
            throw new InvalidOperationException(
                "Lakona:StartupActors was removed. Register Startup Actors in HotfixStartup.ConfigureActors with RegisterStartup<TActor, TKey>() or RegisterStartup<TActor, TKey>(selector), and use Lakona:ActorHosts to choose capable nodes.");
        }

        if (section.GetSection("Health").GetSection("Http").Exists())
        {
            throw new InvalidOperationException(
                "Lakona:Health:Http was removed. Configure the shared listener under Lakona:Management:Http, and configure health route policy under Lakona:Health.");
        }

        return new LakonaGameRuntimeOptions
        {
            Node = BindNode(section.GetSection("Node")),
            Endpoints = BindEndpoints(section.GetSection("Endpoints")),
            Http = BindHttp(section.GetSection("Http")),
            ActorHosts = BindStringArray(section.GetSection("ActorHosts")),
            Cluster = BindCluster(section.GetSection("Cluster")),
            Heartbeat = LakonaGameHeartbeatOptions.FromConfiguration(section.GetSection("Heartbeat")),
            Sessions = LakonaSessionHostingOptions.FromConfiguration(section.GetSection("Sessions")),
            ReliablePush = BindReliablePush(section.GetSection("ReliablePush")),
            Notifications = LakonaNotificationOptions.FromConfiguration(
                section.GetSection("Notifications")),
            Health = LakonaHealthOptions.FromConfiguration(section.GetSection("Health")),
            Management = LakonaManagementOptions.FromConfiguration(section.GetSection("Management")),
            Observability = LakonaObservabilityOptions.FromConfiguration(configuration)
        };
    }

    private static ReliablePushOptions BindReliablePush(IConfiguration section)
    {
        var defaults = new ReliablePushOptions();
        return new ReliablePushOptions
        {
            MaxPendingPerSession = LakonaConfigurationReader.ReadInt(
                section,
                "MaxPendingPerSession",
                defaults.MaxPendingPerSession)
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
                ReliablePush = LakonaConfigurationReader.ReadBool(endpoint, "ReliablePush", false),
                RpcServices = BindStringArray(endpoint.GetSection("RpcServices"))
            })
            .ToArray();
    }

    private static LakonaHttpOptions BindHttp(IConfigurationSection section)
    {
        var listenersSection = section.GetSection("Listeners");
        IReadOnlyList<LakonaHttpListenerOptions> listeners;
        if (TryReadJsonValue(listenersSection, out var json))
        {
            RejectRemovedHttpExposure(listenersSection.Path, json);
            listeners = ParseJsonArray<LakonaHttpListenerOptions>(listenersSection.Path, json);
        }
        else
        {
            var listenerSections = listenersSection.GetChildren().ToArray();
            foreach (var listener in listenerSections)
            {
                if (listener.GetSection("Exposure").Exists())
                {
                    throw new InvalidOperationException(
                        $"{listener.Path}:Exposure was removed. Use the bind address and deployment network to control listener exposure.");
                }
            }

            listeners = listenerSections
                .Select(listener => new LakonaHttpListenerOptions
                {
                    Id = listener["Id"] ?? "",
                    Host = listener["Host"] ?? "",
                    Port = LakonaConfigurationReader.ReadInt(listener["Port"]),
                    Services = BindStringArray(listener.GetSection("Services")),
                    MaximumBodyBytes = LakonaConfigurationReader.ReadInt(
                        listener,
                        "MaximumBodyBytes",
                        LakonaHttpListenerOptions.DefaultMaximumBodyBytes),
                    RequestTimeoutSeconds = LakonaConfigurationReader.ReadInt(
                        listener,
                        "RequestTimeoutSeconds",
                        LakonaHttpListenerOptions.DefaultRequestTimeoutSeconds)
                })
                .ToArray();
        }

        LakonaHttpOptions.Validate(listeners);
        return new LakonaHttpOptions { Listeners = listeners };
    }

    private static void RejectRemovedHttpExposure(string path, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var index = 0;
            foreach (var listener in document.RootElement.EnumerateArray())
            {
                if (listener.ValueKind == JsonValueKind.Object
                    && TryGetPropertyIgnoreCase(listener, "Exposure", out _))
                {
                    throw new InvalidOperationException(
                        $"{path}:{index}:Exposure was removed. Use the bind address and deployment network to control listener exposure.");
                }

                index++;
            }
        }
        catch (JsonException)
        {
            // ParseJsonArray owns the canonical malformed-JSON diagnostic.
        }
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
                    Endpoint = section.Value
                };
            }

            return LakonaGameClusterOptions.Defaults();
        }

        return new LakonaGameClusterOptions
        {
            Endpoint = ReadClusterString(section, "Endpoint", LakonaGameClusterOptions.DefaultEndpoint),
            BootstrapNewCluster = LakonaConfigurationReader.ReadBool(
                section,
                "BootstrapNewCluster",
                false),
            Seeds = BindStringArray(section.GetSection("Seeds"))
        };
    }

    private static string ReadClusterString(IConfiguration section, string name, string fallback)
    {
        return section[name] ?? fallback;
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

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string name,
        out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
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

public sealed class LakonaHealthOptions
{
    public bool Enabled { get; init; }

    public bool RequireLoopback { get; init; } = true;

    public static LakonaHealthOptions Defaults()
    {
        return new LakonaHealthOptions();
    }

    public static LakonaHealthOptions FromConfiguration(IConfigurationSection section)
    {
        return new LakonaHealthOptions
        {
            Enabled = LakonaConfigurationReader.ReadBool(section, "Enabled", false),
            RequireLoopback = LakonaConfigurationReader.ReadBool(section, "RequireLoopback", true)
        };
    }
}

public sealed class LakonaManagementOptions
{
    public LakonaManagementHttpOptions Http { get; init; } = LakonaManagementHttpOptions.Defaults();

    public static LakonaManagementOptions Defaults()
    {
        return new LakonaManagementOptions();
    }

    public static LakonaManagementOptions FromConfiguration(IConfigurationSection section)
    {
        return new LakonaManagementOptions
        {
            Http = LakonaManagementHttpOptions.FromConfiguration(section.GetSection("Http"))
        };
    }
}

public sealed class LakonaManagementHttpOptions
{
    public string Host { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 20080;

    public static LakonaManagementHttpOptions Defaults()
    {
        return new LakonaManagementHttpOptions();
    }

    public static LakonaManagementHttpOptions FromConfiguration(IConfiguration section)
    {
        return new LakonaManagementHttpOptions
        {
            Host = LakonaConfigurationReader.ReadString(section, "Host", "127.0.0.1"),
            Port = LakonaConfigurationReader.ReadInt(section, "Port", 20080)
        };
    }
}
