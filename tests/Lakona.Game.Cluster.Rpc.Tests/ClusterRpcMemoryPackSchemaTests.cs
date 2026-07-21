using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ClusterRpcMemoryPackSchemaTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly Regex FormatterClassPattern = new(
        @"class\s+@?\w+Formatter\s*:\s*(?:global::)?(?:MemoryPack\.)?MemoryPackFormatter\b",
        RegexOptions.CultureInvariant);

    private static readonly string[] ExpectedTypeNames =
    [
        "NodeEndpointDto",
        "NodeActorHostDto",
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
        "ClientNotificationDispatchRequest",
        "ClientNotificationDispatchReply",
        "ClientNotificationBatchDispatchRequest",
        "ClientNotificationBatchDispatchReply",
        "ClientNotificationCommand",
        "ClientNotificationArgument",
        "StartupActorDto",
        "ClusterMembershipFrameRequest",
        "ClusterMembershipFrameReply"
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
        var sourceRoot = Path.Combine(RepositoryRoot, "src", "Lakona.Game.Cluster.Rpc.Serializer.MemoryPack");

        var formatterFiles = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedOutput(path))
            .Where(path => ContainsHandWrittenMemoryPackFormatter(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'))
            .ToArray();

        Assert.Empty(formatterFiles);
    }

    [Fact]
    public void Cluster_rpc_tests_do_not_preserve_legacy_memorypack_wire_contracts()
    {
        var testRoot = Path.Combine(RepositoryRoot, "tests", "Lakona.Game.Cluster.Rpc.Tests");
        var marker = "matches_legacy_memorypack" + "_wire_bytes";
        var legacyContractFiles = Directory
            .EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                marker,
                StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(testRoot, path).Replace('\\', '/'))
            .ToArray();

        Assert.Empty(legacyContractFiles);
    }

    [Theory]
    [InlineData("internal sealed class RouteFormatter : MemoryPackFormatter<RouteRegisterRequest>")]
    [InlineData("internal sealed class RouteFormatter : MemoryPack.MemoryPackFormatter<RouteRegisterRequest>")]
    [InlineData("internal sealed class RouteFormatter : global::MemoryPack.MemoryPackFormatter<RouteRegisterRequest>")]
    [InlineData("file sealed class @RouteRegisterRequestFormatter : global::MemoryPack.MemoryPackFormatter<RouteRegisterRequest>")]
    public void Formatter_source_scan_detects_qualified_memorypack_formatter_base_types(string source)
    {
        Assert.True(ContainsHandWrittenMemoryPackFormatter(source));
    }

    [Theory]
    [InlineData("Generated/RouteRegisterRequestFormatter.cs", false)]
    [InlineData("bin/Debug/net9.0/Generated/RouteRegisterRequestFormatter.cs", true)]
    [InlineData("obj/Debug/net9.0/Generated/RouteRegisterRequestFormatter.cs", true)]
    public void Formatter_source_scan_only_ignores_compiler_output_paths(string relativePath, bool expectedIgnored)
    {
        var sourceRoot = Path.Combine(RepositoryRoot, "src", "Lakona.Game.Cluster.Rpc.Serializer.MemoryPack");
        var path = Path.Combine(sourceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.Equal(expectedIgnored, IsGeneratedOutput(path));
    }

    private static ClusterRpcMemoryPackSchema LoadSchema()
    {
        var schemaPath = Path.Combine(
            RepositoryRoot,
            "src",
            "Lakona.Game.Cluster.Rpc.Serializer.MemoryPack",
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
            string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsHandWrittenMemoryPackFormatter(string source)
    {
        return FormatterClassPattern.IsMatch(source);
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
