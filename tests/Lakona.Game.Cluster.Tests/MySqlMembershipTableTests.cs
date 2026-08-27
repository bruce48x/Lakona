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
            await ApplyMembershipSchemaAsync();
            Assert.Equal(1, (await table.AllocateGenerationAsync(
                "Release1",
                TestContext.Current.CancellationToken)).Value);
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

    private async ValueTask<MySqlMembershipTable?> CreateIsolatedTableAsync(bool applySchema)
    {
        adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            Assert.Skip($"Set {ConnectionEnvironmentVariable} to run the MySQL Membership contract.");
            return null;
        }

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
