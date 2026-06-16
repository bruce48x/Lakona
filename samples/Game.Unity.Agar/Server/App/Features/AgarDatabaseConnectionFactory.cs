using System.Data.Common;
using Npgsql;

namespace Server.App.Features;

public sealed class AgarDatabaseConnectionFactory
{
    private readonly AgarDatabaseOptions _options;

    public AgarDatabaseConnectionFactory(AgarDatabaseOptions options)
    {
        _options = options;
    }

    public DbConnection CreatePostgresConnection()
    {
        return new NpgsqlConnection(_options.PostgresConnectionString);
    }
}
