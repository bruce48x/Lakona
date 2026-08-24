using Lakona.Game.Cluster.Membership;
using Npgsql;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

[Trait("Category", "PostgresIntegration")]
public sealed class PostgresMembershipTableTests : MembershipTableContractTests
{
    private const string ConnectionEnvironmentVariable = "LAKONA_TEST_POSTGRES_CONNECTION";

    private protected override ValueTask<IMembershipTable?> CreateTableAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip($"Set {ConnectionEnvironmentVariable} to run the PostgreSQL Membership contract.");
            return default;
        }

        return new ValueTask<IMembershipTable?>(
            new PostgresMembershipTable(NpgsqlDataSource.Create(connectionString)));
    }
}
