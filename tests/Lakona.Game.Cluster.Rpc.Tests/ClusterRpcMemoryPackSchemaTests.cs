using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ClusterRpcMemoryPackSchemaTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string[] ExpectedTypeNames =
    [
        "NodeEndpointDto",
        "NodeFeatureDto",
        "NodeRegistrationDto",
        "NodeRecordDto",
        "NodeDirectoryClientQueryDto",
        "NodeRegisterRequest",
        "NodeRegisterReply",
        "NodeHeartbeatRequest",
        "NodeHeartbeatReply",
        "NodeUpdateStateRequest",
        "NodeUpdateStateReply",
        "NodeResolveRequest",
        "NodeResolveReply",
        "NodeQueryRequest",
        "NodeQueryReply",
        "NodeExpireRequest",
        "NodeExpireReply",
        "RouteLocationDto",
        "RouteRegisterRequest",
        "RouteRegisterReply",
        "RouteResolveRequest",
        "RouteResolveReply",
        "RouteUnregisterRequest",
        "RouteUnregisterReply",
        "RouteRefreshLeaseRequest",
        "RouteRefreshLeaseReply",
        "RouteExpireRequest",
        "RouteExpireReply",
        "RouteClearByNodeRequest",
        "RouteClearByNodeEpochRequest",
        "RouteClearReply",
        "ClusterSendRequest",
        "ClusterSendReply",
        "FeatureSendRequest",
        "FeatureSendReply",
        "ClientNotificationDispatchRequest",
        "ClientNotificationDispatchReply",
        "ClientNotificationCommand",
        "ClientNotificationArgument"
    ];

    [Fact]
    public void Schema_type_list_matches_work_item_5_contract()
    {
        var schema = LoadSchema();

        Assert.Equal(ExpectedTypeNames, schema.Types.Select(type => type.Name));
    }

    [Fact]
    public void Schema_properties_match_public_instance_property_order()
    {
        var schema = LoadSchema();
        var assembly = typeof(ClusterSendRequest).Assembly;

        foreach (var schemaType in schema.Types)
        {
            var dtoType = assembly.GetType("Lakona.Game.Cluster.Rpc." + schemaType.Name, throwOnError: true)!;
            var propertyNames = dtoType
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .OrderBy(property => property.MetadataToken)
                .Select(property => property.Name)
                .ToArray();

            Assert.Equal(schemaType.Properties, propertyNames);
        }
    }

    [Fact]
    public void MemoryPack_package_commits_no_hand_written_per_dto_formatters()
    {
        var sourceRoot = Path.Combine(RepositoryRoot, "src", "Lakona.Game.Cluster.Rpc.MemoryPack");
        var formatterClassPattern = new Regex(
            @"class\s+\w+Formatter\s*:\s*MemoryPackFormatter",
            RegexOptions.CultureInvariant);

        var formatterFiles = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedOutput(path))
            .Where(path => formatterClassPattern.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'))
            .ToArray();

        Assert.Empty(formatterFiles);
    }

    private static ClusterRpcMemoryPackSchema LoadSchema()
    {
        var schemaPath = Path.Combine(
            RepositoryRoot,
            "src",
            "Lakona.Game.Cluster.Rpc.MemoryPack",
            "Generation",
            "cluster-rpc-memorypack.schema.json");
        var json = File.ReadAllText(schemaPath);
        var schema = JsonSerializer.Deserialize<ClusterRpcMemoryPackSchema>(json);
        Assert.NotNull(schema);
        return schema;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Lakona.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static bool IsGeneratedOutput(string path)
    {
        var relative = Path.GetRelativePath(RepositoryRoot, path);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, "Generated", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ClusterRpcMemoryPackSchema
    {
        [JsonPropertyName("types")]
        public required ClusterRpcMemoryPackSchemaType[] Types { get; init; }
    }

    private sealed class ClusterRpcMemoryPackSchemaType
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("properties")]
        public required string[] Properties { get; init; }
    }
}
