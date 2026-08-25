using Lakona.Game.Cluster.Membership;
using Npgsql;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

[Trait("Category", "PostgresIntegration")]
public sealed class PostgresMembershipTableTests : MembershipTableContractTests
{
    private const string ConnectionEnvironmentVariable = "LAKONA_TEST_POSTGRES_CONNECTION";
    private string? connectionString;
    private string? schemaName;

    private protected override async ValueTask<IMembershipTable?> CreateTableAsync()
    {
        connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip($"Set {ConnectionEnvironmentVariable} to run the PostgreSQL Membership contract.");
            return null;
        }

        schemaName = $"lakona_membership_test_{Guid.NewGuid():N}";
        await using (var setup = NpgsqlDataSource.Create(connectionString))
        await using (var command = setup.CreateCommand($"CREATE SCHEMA \"{schemaName}\";"))
        {
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var isolated = new NpgsqlConnectionStringBuilder(connectionString)
        {
            SearchPath = schemaName
        };
        return new PostgresMembershipTable(NpgsqlDataSource.Create(isolated.ConnectionString));
    }

    private protected override async ValueTask DisposeTableAsync(IMembershipTable table)
    {
        await base.DisposeTableAsync(table);
        if (connectionString is null || schemaName is null) return;
        await using var cleanup = NpgsqlDataSource.Create(connectionString);
        await using var command = cleanup.CreateCommand($"DROP SCHEMA \"{schemaName}\" CASCADE;");
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
