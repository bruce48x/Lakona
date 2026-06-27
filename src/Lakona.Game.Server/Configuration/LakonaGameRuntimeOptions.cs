using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Lakona.Game.Server.Configuration;

public sealed class LakonaGameRuntimeOptions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public LakonaGameNodeOptions Node { get; init; } = new();
    public IReadOnlyList<LakonaGameEndpointOptions> Endpoints { get; init; } = [];
    public IReadOnlyList<string>? Feature { get; init; }
    public LakonaGameClusterOptions? Cluster { get; init; }
    public string ClusterEndpoint { get; init; } = "tcp://127.0.0.1:21000";

    public static LakonaGameRuntimeOptions FromConfiguration(IConfiguration configuration)
    {
        var section = GetRuntimeSection(configuration);

        return new LakonaGameRuntimeOptions
        {
            Node = BindNode(section.GetSection("Node")),
            Endpoints = BindEndpoints(section.GetSection("Endpoints")),
            Feature = BindOptionalStringArray(section.GetSection("Feature")),
            Cluster = BindCluster(section.GetSection("Cluster"))
        };
    }

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

    public ClusterOptions ToClusterOptions(IConfiguration configuration)
    {
        var defaults = ToClusterOptions();
        var section = GetRuntimeSection(configuration).GetSection("Cluster");

        return new ClusterOptions
        {
            NodeId = ReadString(section, "NodeId", defaults.NodeId),
            AdvertisedEndpoints = ReadDictionary(
                section.GetSection("AdvertisedEndpoints"), defaults.AdvertisedEndpoints),
            Bootstrap = ClusterBootstrapOptions.FromConfiguration(
                section.GetSection("Bootstrap"), defaults.Bootstrap),
            NodeDirectory = ClusterNodeDirectoryOptions.FromConfiguration(
                section.GetSection("NodeDirectory"), defaults.NodeDirectory),
            RouteLeaseSeconds = ReadInt(section, "RouteLeaseSeconds", defaults.RouteLeaseSeconds),
            SendTimeoutMilliseconds = ReadInt(section, "SendTimeoutMilliseconds", defaults.SendTimeoutMilliseconds)
        };
    }

    private static IConfigurationSection GetRuntimeSection(IConfiguration configuration)
    {
        var lakona = configuration.GetSection("Lakona");
        if (lakona.GetChildren().Any() || lakona.Value is not null)
        {
            return lakona;
        }

        return configuration.GetSection("Lakona.Game");
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
                Port = ReadInt(endpoint["Port"]),
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
            NodeTable = ReadString(section, "NodeTable", "lakona_cluster_nodes"),
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

    private static int ReadInt(string? value)
    {
        return int.TryParse(value, out var parsed) ? parsed : 0;
    }

    private static string ReadString(IConfiguration section, string name, string fallback)
    {
        var value = section[name];
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static int ReadInt(IConfiguration section, string name, int fallback)
    {
        return int.TryParse(section[name], out var parsed) ? parsed : fallback;
    }

    private static IReadOnlyDictionary<string, string> ReadDictionary(
        IConfigurationSection section,
        IReadOnlyDictionary<string, string> fallback)
    {
        var children = section.GetChildren().ToList();
        if (children.Count == 0)
        {
            return fallback;
        }

        return children.ToDictionary(child => child.Key, child => child.Value ?? "");
    }

}

public sealed class LakonaGameNodeOptions
{
    public string Id { get; init; } = "dev-1";

    public static LakonaGameNodeOptions FromConfiguration(IConfiguration section)
    {
        return new LakonaGameNodeOptions
        {
            Id = ReadString(section, "Id", "dev-1")
        };
    }

    private static string ReadString(IConfiguration section, string name, string fallback)
    {
        var value = section[name];
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
