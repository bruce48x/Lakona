namespace Server.App.Features;

public sealed class AgarDatabaseOptions
{
    public const string PostgresConnectionName = "AgarPostgres";
    public const string RedisConnectionName = "AgarRedis";
    public const string DefaultNodeDirectoryTable = "lakona_cluster_nodes";

    public AgarDatabaseOptions(
        string postgresConnectionString,
        string redisConnectionString,
        string nodeDirectoryTable,
        bool ensureSchemaOnStartup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postgresConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(redisConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeDirectoryTable);

        PostgresConnectionString = postgresConnectionString;
        RedisConnectionString = redisConnectionString;
        NodeDirectoryTable = nodeDirectoryTable;
        EnsureSchemaOnStartup = ensureSchemaOnStartup;
    }

    public string PostgresConnectionString { get; }

    public string RedisConnectionString { get; }

    public string NodeDirectoryTable { get; }

    public bool EnsureSchemaOnStartup { get; }
}
