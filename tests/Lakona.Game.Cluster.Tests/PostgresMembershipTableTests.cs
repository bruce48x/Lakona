using Lakona.Game.Cluster.Membership;
using Lakona.Game.Clustering.Postgres;
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
        var table = await CreateIsolatedTableAsync(applySchema: true);
        if (table is null)
        {
            return null;
        }

        return table;
    }

    [Fact]
    public async Task MembershipSchemaCanBeAppliedMoreThanOnce()
    {
        var table = await CreateIsolatedTableAsync(applySchema: true);
        if (table is null) return;

        try
        {
            await ApplyMembershipSchemaAsync();
            var generation = await table.AllocateGenerationAsync(
                "Release1",
                TestContext.Current.CancellationToken);

            Assert.Equal(1, generation.Value);
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

            Assert.Contains("database/postgresql/membership.sql", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            await DisposeTableAsync(table);
        }
    }

    [Fact]
    public async Task MembershipSchemaReplacesLegacyClusterNamespaceLayout()
    {
        var table = await CreateIsolatedTableAsync(applySchema: false);
        if (table is null) return;

        try
        {
            var isolated = new NpgsqlConnectionStringBuilder(connectionString!)
            {
                SearchPath = schemaName
            };
            await using (var setup = NpgsqlDataSource.Create(isolated.ConnectionString))
            await using (var command = setup.CreateCommand(
                """
                CREATE TABLE lakona_membership_cluster (
                    cluster_id text PRIMARY KEY,
                    incarnation uuid NOT NULL,
                    build_tag text NULL,
                    version bigint NOT NULL,
                    next_generation bigint NOT NULL
                );
                CREATE TABLE lakona_membership_member (
                    cluster_id text NOT NULL,
                    node_id text NOT NULL,
                    node_incarnation uuid NOT NULL,
                    generation bigint NOT NULL,
                    status smallint NOT NULL,
                    entry_version bigint NOT NULL,
                    i_am_alive timestamptz NOT NULL,
                    payload jsonb NOT NULL,
                    PRIMARY KEY (cluster_id, node_id, node_incarnation)
                );
                """))
            {
                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            await ApplyMembershipSchemaAsync();
            var generation = await table.AllocateGenerationAsync(
                "Release1",
                TestContext.Current.CancellationToken);

            Assert.Equal(1, generation.Value);
        }
        finally
        {
            await DisposeTableAsync(table);
        }
    }

    private async ValueTask<PostgresMembershipTable?> CreateIsolatedTableAsync(bool applySchema)
    {
        connectionString = MembershipIntegrationTestEnvironment.RequireConnectionString(
            ConnectionEnvironmentVariable);

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
        var table = new PostgresMembershipTable(NpgsqlDataSource.Create(isolated.ConnectionString));
        if (applySchema)
        {
            await ApplyMembershipSchemaAsync();
        }

        return table;
    }

    private async Task ApplyMembershipSchemaAsync()
    {
        var isolated = new NpgsqlConnectionStringBuilder(connectionString!)
        {
            SearchPath = schemaName
        };
        var sqlPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Lakona.Game.Clustering.Postgres",
            "database",
            "postgresql",
            "membership.sql");
        var sql = await File.ReadAllTextAsync(sqlPath, TestContext.Current.CancellationToken);
        await using var setup = NpgsqlDataSource.Create(isolated.ConnectionString);
        await using var command = setup.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private protected override async ValueTask DisposeTableAsync(IMembershipTable table)
    {
        await base.DisposeTableAsync(table);
        if (connectionString is null || schemaName is null) return;
        await using var cleanup = NpgsqlDataSource.Create(connectionString);
        await using var command = cleanup.CreateCommand($"DROP SCHEMA \"{schemaName}\" CASCADE;");
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository root from '{AppContext.BaseDirectory}'.");
    }
}
