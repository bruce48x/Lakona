using Lakona.Game.Cluster.Membership;
using MySqlConnector;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

[Trait("Category", "MySqlIntegration")]
public sealed class MySqlMembershipTableTests : MembershipTableContractTests
{
    private const string ConnectionEnvironmentVariable = "LAKONA_TEST_MYSQL_CONNECTION";
    private string? adminConnectionString;
    private string? databaseName;

    private protected override async ValueTask<IMembershipTable?> CreateTableAsync() =>
        await CreateIsolatedTableAsync(applySchema: true);

    [Fact]
    public async Task MembershipSchemaCanBeAppliedMoreThanOnce()
    {
        var table = await CreateIsolatedTableAsync(applySchema: true);
        if (table is null) return;
        try
        {
            var first = await table.AllocateGenerationAsync("Release1", TestContext.Current.CancellationToken);
            await ApplyMembershipSchemaAsync();
            var next = await table.AllocateGenerationAsync("Release1", TestContext.Current.CancellationToken);
            Assert.Equal(first.Cluster, next.Cluster);
            Assert.Equal(first.Value + 1, next.Value);
        }
        finally
        {
            await DisposeTableAsync(table);
        }
    }

    [Fact]
    public async Task MissingSchemaFailsWithDeploymentInstructions()
    {
        var table = await CreateIsolatedTableAsync(applySchema: false);
        if (table is null) return;
        try
        {
            var exception = await Assert.ThrowsAsync<MembershipSchemaException>(() =>
                table.ReadOrCreateAsync(TestContext.Current.CancellationToken).AsTask());
            Assert.Contains("database/mysql/membership.sql", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            await DisposeTableAsync(table);
        }
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("missing-index")]
    [InlineData("unknown-marker")]
    public async Task IncompatibleInstallationIsRejectedWithoutChangingStoredData(string fault)
    {
        var partial = fault == "partial";
        var table = await CreateIsolatedTableAsync(applySchema: !partial);
        if (table is null) return;
        try
        {
            var isolated = new MySqlConnectionStringBuilder(adminConnectionString!) { Database = databaseName };
            await using var connection = new MySqlConnection(isolated.ConnectionString);
            var ct = TestContext.Current.CancellationToken;
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            if (!partial)
            {
                await table.AllocateGenerationAsync("Release1", ct);
                command.CommandText = fault == "missing-index"
                    ? "DROP INDEX ux_lakona_membership_live_node ON lakona_membership_member;"
                    : "ALTER TABLE lakona_membership_cluster COMMENT = 'unknown-format';";
            }
            else
            {
                command.CommandText = """
                    CREATE TABLE lakona_membership_cluster (cluster_id varchar(64) PRIMARY KEY);
                    INSERT INTO lakona_membership_cluster VALUES ('keep-me');
                    """;
            }
            await command.ExecuteNonQueryAsync(ct);

            var error = await Assert.ThrowsAsync<MySqlException>(ApplyMembershipSchemaAsync);
            Assert.Contains("ck_membership_compatible_schema", error.Message);
            command.CommandText = partial
                ? "SELECT cluster_id FROM lakona_membership_cluster;"
                : "SELECT build_tag FROM lakona_membership_cluster;";
            Assert.Equal(partial ? "keep-me" : "Release1", await command.ExecuteScalarAsync(ct));
            if (fault == "unknown-marker")
            {
                command.CommandText = "SELECT table_comment FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'lakona_membership_cluster';";
                Assert.Equal("unknown-format", await command.ExecuteScalarAsync(ct));
            }
        }
        finally
        {
            await DisposeTableAsync(table);
        }
    }

    private async ValueTask<MySqlMembershipTable?> CreateIsolatedTableAsync(bool applySchema)
    {
        adminConnectionString = MembershipIntegrationTestEnvironment.RequireConnectionString(
            ConnectionEnvironmentVariable);

        databaseName = $"lakona_membership_test_{Guid.NewGuid():N}";
        await using (var connection = new MySqlConnection(adminConnectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new MySqlCommand(
                $"CREATE DATABASE `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_bin;",
                connection);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var isolated = new MySqlConnectionStringBuilder(adminConnectionString) { Database = databaseName };
        var table = new MySqlMembershipTable(new MySqlDataSource(isolated.ConnectionString));
        if (applySchema) await ApplyMembershipSchemaAsync();
        return table;
    }

    private async Task ApplyMembershipSchemaAsync()
    {
        var isolated = new MySqlConnectionStringBuilder(adminConnectionString!) { Database = databaseName };
        var sqlPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Lakona.Game.Clustering.MySql",
            "database",
            "mysql",
            "membership.sql");
        var sql = await File.ReadAllTextAsync(sqlPath, TestContext.Current.CancellationToken);
        await using var connection = new MySqlConnection(isolated.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private protected override async ValueTask DisposeTableAsync(IMembershipTable table)
    {
        await base.DisposeTableAsync(table);
        if (adminConnectionString is null || databaseName is null) return;
        await using var connection = new MySqlConnection(adminConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = new MySqlCommand($"DROP DATABASE `{databaseName}`;", connection);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException($"Could not find repository root from '{AppContext.BaseDirectory}'.");
    }
}
