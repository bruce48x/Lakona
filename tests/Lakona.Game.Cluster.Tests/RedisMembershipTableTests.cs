using Lakona.Game.Cluster.Membership;
using StackExchange.Redis;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

[Trait("Category", "RedisIntegration")]
public sealed class RedisMembershipTableTests : MembershipTableContractTests
{
    private const string ConnectionEnvironmentVariable = "LAKONA_TEST_REDIS_CONNECTION";
    private IConnectionMultiplexer? connection;
    private string? key;

    [Fact]
    public async Task MembershipKeyNeverExpires()
    {
        var table = await CreateTableAsync();
        if (table is null) return;
        try
        {
            await table.AllocateGenerationAsync("Release1", TestContext.Current.CancellationToken);
            Assert.Null(await connection!.GetDatabase().KeyTimeToLiveAsync(key!));
        }
        finally
        {
            await DisposeTableAsync(table);
        }
    }

    [Fact]
    public async Task IncompatibleSchemaMarkerFailsWithoutOverwritingData()
    {
        var table = await CreateTableAsync();
        if (table is null) return;
        try
        {
            await connection!.GetDatabase().HashSetAsync(key!, "schema", "99");

            var exception = await Assert.ThrowsAsync<MembershipSchemaException>(() =>
                table.ReadOrCreateAsync(TestContext.Current.CancellationToken).AsTask());

            Assert.Contains("incompatible schema marker", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("99", await connection.GetDatabase().HashGetAsync(key!, "schema"));
        }
        finally
        {
            await DisposeTableAsync(table);
        }
    }

    [Fact]
    public void IncompatibleSchemaMarkerUsesFailFastException()
    {
        var exception = Assert.Throws<MembershipSchemaException>(() =>
            RedisMembershipTable.EnsureSchema(0, "99"));

        Assert.Contains("incompatible schema marker", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private protected override async ValueTask<IMembershipTable?> CreateTableAsync()
    {
        var connectionString = MembershipIntegrationTestEnvironment.RequireConnectionString(
            ConnectionEnvironmentVariable);

        connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        key = $"lakona:{{membership-test}}:{Guid.NewGuid():N}";
        return new RedisMembershipTable(connection, key);
    }

    private protected override async ValueTask DisposeTableAsync(IMembershipTable table)
    {
        if (connection is not null && key is not null)
        {
            await connection.GetDatabase().KeyDeleteAsync(key);
        }
        await base.DisposeTableAsync(table);
        connection = null;
        key = null;
    }
}
