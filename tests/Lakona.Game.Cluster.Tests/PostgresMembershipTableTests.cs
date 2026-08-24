using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Membership;
using Npgsql;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class PostgresMembershipTableTests
{
    private const string ConnectionEnvironmentVariable = "LAKONA_TEST_POSTGRES_CONNECTION";

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task PostgreSqlImplementsGenerationCasReplacementHeartbeatAndCleanupContract()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        await using var table = new PostgresMembershipTable(NpgsqlDataSource.Create(connectionString));
        var cancellationToken = TestContext.Current.CancellationToken;
        var clusterId = $"contract-{Guid.NewGuid():N}";
        var firstGeneration = await table.AllocateGenerationAsync(clusterId, cancellationToken);
        var secondGeneration = await table.AllocateGenerationAsync(clusterId, cancellationToken);
        Assert.Equal(firstGeneration.Cluster, secondGeneration.Cluster);
        Assert.Equal(firstGeneration.Value + 1, secondGeneration.Value);

        var initial = await table.ReadOrCreateAsync(clusterId, cancellationToken);
        var first = Joining(
            initial.Cluster,
            "server-1",
            "11111111-1111-1111-1111-111111111111",
            firstGeneration.Value);
        var competing = Joining(
            initial.Cluster,
            "server-2",
            "22222222-2222-2222-2222-222222222222",
            secondGeneration.Value);
        var inserts = await Task.WhenAll(
            table.TryInsertAsync(clusterId, first, initial.Version, cancellationToken).AsTask(),
            table.TryInsertAsync(clusterId, competing, initial.Version, cancellationToken).AsTask());
        Assert.Single(inserts, static committed => committed);

        var inserted = await table.ReadOrCreateAsync(clusterId, cancellationToken);
        var winner = Assert.Single(inserted.Entries);
        var active = winner.WithStatus(MembershipTableStatus.Active);
        Assert.True(await table.TryUpdateAsync(
            clusterId,
            active,
            winner.Version,
            inserted.Version,
            cancellationToken));
        var activated = await table.ReadOrCreateAsync(clusterId, cancellationToken);
        var invalidRegression = new MembershipTableEntry(
            active.Reference,
            MembershipTableStatus.Joining,
            active.ClusterEndpoint,
            active.Version + 1,
            active.IAmAliveTime,
            startTime: active.StartTime,
            generation: active.Generation);
        Assert.False(await table.TryUpdateAsync(
            clusterId,
            invalidRegression,
            active.Version,
            activated.Version,
            cancellationToken));
        var heartbeat = winner.IAmAliveTime.AddMinutes(1);
        Assert.True(await table.TryUpdateIAmAliveAsync(clusterId, active.Reference, heartbeat, cancellationToken));
        Assert.Equal(activated.Version, (await table.ReadOrCreateAsync(clusterId, cancellationToken)).Version);

        var replacementGeneration = await table.AllocateGenerationAsync(clusterId, cancellationToken);
        var replacement = Joining(
            initial.Cluster,
            active.Reference.Node.Value,
            "33333333-3333-3333-3333-333333333333",
            replacementGeneration.Value);
        Assert.True(await table.TryReplaceAsync(
            clusterId,
            active.Reference,
            active.Version,
            replacement,
            activated.Version,
            cancellationToken));

        var replaced = await table.ReadOrCreateAsync(clusterId, cancellationToken);
        Assert.Equal(MembershipTableStatus.Dead, replaced.Entries.Single(entry => entry.Reference == active.Reference).Status);
        Assert.Equal(MembershipTableStatus.Joining, replaced.Entries.Single(entry => entry.Reference == replacement.Reference).Status);
        Assert.Equal(1, await table.CleanupDefunctAsync(
            clusterId,
            heartbeat.AddMinutes(1),
            maximumRows: 1,
            cancellationToken));
        Assert.Equal(replacement.Reference, Assert.Single((await table.ReadOrCreateAsync(clusterId, cancellationToken)).Entries).Reference);
    }

    private static MembershipTableEntry Joining(
        ClusterIncarnationId cluster,
        string node,
        string incarnation,
        long generation) =>
        new(
            new NodeReference(cluster, new NodeId(node), new NodeIncarnationId(Guid.Parse(incarnation))),
            MembershipTableStatus.Joining,
            new NodeEndpoint("tcp://127.0.0.1:21001"),
            version: 1,
            iAmAliveTime: DateTimeOffset.Parse("2026-08-24T00:00:00Z"),
            generation: generation);
}
