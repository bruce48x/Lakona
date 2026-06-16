namespace Server.App.Features;

public sealed class AgarDatabaseOptions
{
    public const string PostgresConnectionName = "AgarPostgres";
    public const string RedisConnectionName = "AgarRedis";
    public const string DefaultNodeDirectoryTable = "lakona_game_cluster_nodes";

    public AgarDatabaseOptions(
        string postgresConnectionString,
        string redisConnectionString,
        string nodeDirectoryTable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postgresConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(redisConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeDirectoryTable);

        PostgresConnectionString = postgresConnectionString;
        RedisConnectionString = redisConnectionString;
        NodeDirectoryTable = nodeDirectoryTable;
    }

    public string PostgresConnectionString { get; }

    public string RedisConnectionString { get; }

    public string NodeDirectoryTable { get; }
}
