using System.Data;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Lakona.Game.Cluster.Membership;

internal sealed class PostgresMembershipTable : IMembershipTable, IAsyncDisposable
{
    private const string CreateSchemaSql = """
        CREATE TABLE IF NOT EXISTS lakona_membership_cluster (
            singleton boolean PRIMARY KEY CHECK (singleton),
            incarnation uuid NOT NULL,
            version bigint NOT NULL CHECK (version >= 0),
            next_generation bigint NOT NULL CHECK (next_generation > 0)
        );
        CREATE TABLE IF NOT EXISTS lakona_membership_member (
            node_id text NOT NULL,
            node_incarnation uuid NOT NULL,
            generation bigint NOT NULL CHECK (generation > 0),
            status smallint NOT NULL,
            entry_version bigint NOT NULL CHECK (entry_version > 0),
            i_am_alive timestamptz NOT NULL,
            payload jsonb NOT NULL,
            PRIMARY KEY (node_id, node_incarnation)
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_lakona_membership_live_node
            ON lakona_membership_member(node_id) WHERE status <> 3;
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource dataSource;
    private readonly SemaphoreSlim schemaGate = new(1, 1);
    private volatile bool schemaReady;

    public PostgresMembershipTable(NpgsqlDataSource dataSource) =>
        this.dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    public async ValueTask<MembershipTableGeneration> AllocateGenerationAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var create = new NpgsqlCommand(
            "INSERT INTO lakona_membership_cluster(singleton, incarnation, version, next_generation) VALUES (TRUE, $1, 0, 1) ON CONFLICT (singleton) DO NOTHING;",
            connection,
            transaction))
        {
            create.Parameters.AddWithValue(Guid.NewGuid());
            await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var allocate = new NpgsqlCommand(
            "UPDATE lakona_membership_cluster SET next_generation = next_generation + 1 WHERE singleton AND next_generation < 9223372036854775807 RETURNING incarnation, next_generation - 1;",
            connection,
            transaction);
        await using var reader = await allocate.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Membership generation is exhausted.");
        }

        var result = new MembershipTableGeneration(
            new ClusterIncarnationId(reader.GetGuid(0)),
            reader.GetInt64(1));
        await reader.DisposeAsync().ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async ValueTask<MembershipTableSnapshot> ReadOrCreateAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        await using (var create = new NpgsqlCommand(
            "INSERT INTO lakona_membership_cluster(singleton, incarnation, version, next_generation) VALUES (TRUE, $1, 0, 1) ON CONFLICT (singleton) DO NOTHING;",
            connection,
            transaction))
        {
            create.Parameters.AddWithValue(Guid.NewGuid());
            await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        ClusterIncarnationId cluster;
        MembershipViewId version;
        await using (var metadata = new NpgsqlCommand(
            "SELECT incarnation, version FROM lakona_membership_cluster WHERE singleton FOR SHARE;",
            connection,
            transaction))
        {
            await using var reader = await metadata.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Membership cluster metadata disappeared during a transactional read.");
            }

            cluster = new ClusterIncarnationId(reader.GetGuid(0));
            version = new MembershipViewId(reader.GetInt64(1));
        }

        var entries = new List<MembershipTableEntry>();
        await using (var rows = new NpgsqlCommand(
            "SELECT node_id, node_incarnation, status, entry_version, i_am_alive, payload::text, generation FROM lakona_membership_member ORDER BY node_id, node_incarnation;",
            connection,
            transaction))
        {
            await using var reader = await rows.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                entries.Add(DeserializeEntry(cluster, reader));
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new MembershipTableSnapshot(cluster, version, entries);
    }

    public async ValueTask<bool> TryInsertAsync(
        MembershipTableEntry entry,
        MembershipViewId expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Version != 1 || entry.Status != MembershipTableStatus.Joining)
        {
            return false;
        }

        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await TryAdvanceVersionAsync(
                    connection,
                    transaction,
                    entry.Reference.Cluster,
                    expectedVersion,
                    cancellationToken).ConfigureAwait(false))
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            await using var command = new NpgsqlCommand(
                "INSERT INTO lakona_membership_member(node_id, node_incarnation, status, entry_version, i_am_alive, payload, generation) VALUES ($1, $2, $3, $4, $5, $6, $7);",
                connection,
                transaction);
            AddEntryParameters(command, entry);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    public async ValueTask<bool> TryUpdateAsync(
        MembershipTableEntry entry,
        long expectedEntryVersion,
        MembershipViewId expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (expectedEntryVersion == long.MaxValue || entry.Version != expectedEntryVersion + 1)
        {
            return false;
        }

        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (!await TryAdvanceVersionAsync(
                connection,
                transaction,
                entry.Reference.Cluster,
                expectedVersion,
                cancellationToken).ConfigureAwait(false))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        await using var command = new NpgsqlCommand(
            """
            UPDATE lakona_membership_member
            SET status = $3, entry_version = $4, i_am_alive = $5, payload = $6
            WHERE node_id = $1 AND node_incarnation = $2
              AND generation = $7 AND entry_version = $8
              AND ((status = 0 AND $3 IN (0, 1, 3))
                OR (status = 1 AND $3 IN (1, 2, 3))
                OR (status = 2 AND $3 IN (2, 3)));
            """,
            connection,
            transaction);
        AddEntryParameters(command, entry);
        command.Parameters.AddWithValue(expectedEntryVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> TryReplaceAsync(
        NodeReference previous,
        long expectedPreviousVersion,
        MembershipTableEntry replacement,
        MembershipViewId expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(replacement);
        if (previous.Cluster != replacement.Reference.Cluster
            || previous.Node != replacement.Reference.Node
            || previous == replacement.Reference
            || expectedPreviousVersion == long.MaxValue
            || replacement.Version != 1
            || replacement.Status != MembershipTableStatus.Joining)
        {
            return false;
        }

        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await TryAdvanceVersionAsync(
                    connection,
                    transaction,
                    replacement.Reference.Cluster,
                    expectedVersion,
                    cancellationToken).ConfigureAwait(false))
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            await using (var fence = new NpgsqlCommand(
                "UPDATE lakona_membership_member SET status = 3, entry_version = entry_version + 1 WHERE node_id = $1 AND node_incarnation = $2 AND entry_version = $3 AND generation < $4 AND status <> 3;",
                connection,
                transaction))
            {
                fence.Parameters.AddWithValue(previous.Node.Value);
                fence.Parameters.AddWithValue(previous.Incarnation.Value);
                fence.Parameters.AddWithValue(expectedPreviousVersion);
                fence.Parameters.AddWithValue(replacement.Generation);
                if (await fence.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return false;
                }
            }

            await using (var insert = new NpgsqlCommand(
                "INSERT INTO lakona_membership_member(node_id, node_incarnation, status, entry_version, i_am_alive, payload, generation) VALUES ($1, $2, $3, $4, $5, $6, $7);",
                connection,
                transaction))
            {
                AddEntryParameters(insert, replacement);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    public async ValueTask<bool> TryUpdateIAmAliveAsync(
        NodeReference reference,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var command = dataSource.CreateCommand(
            "UPDATE lakona_membership_member SET i_am_alive = $3 WHERE node_id = $1 AND node_incarnation = $2 AND status <> 3 AND i_am_alive < $3;");
        command.Parameters.AddWithValue(reference.Node.Value);
        command.Parameters.AddWithValue(reference.Incarnation.Value);
        command.Parameters.AddWithValue(timestamp);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async ValueTask<int> CleanupDefunctAsync(
        DateTimeOffset before,
        int maximumRows,
        CancellationToken cancellationToken = default)
    {
        if (maximumRows <= 0) throw new ArgumentOutOfRangeException(nameof(maximumRows));
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var command = dataSource.CreateCommand(
            """
            DELETE FROM lakona_membership_member
            WHERE ctid IN (
                SELECT ctid
                FROM lakona_membership_member
                WHERE status = 3 AND i_am_alive < $1
                ORDER BY i_am_alive
                LIMIT $2
                FOR UPDATE SKIP LOCKED
            );
            """);
        command.Parameters.AddWithValue(before);
        command.Parameters.AddWithValue(maximumRows);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (schemaReady) return;
        await schemaGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (schemaReady) return;
            await using var command = dataSource.CreateCommand(CreateSchemaSql);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            schemaReady = true;
        }
        finally
        {
            schemaGate.Release();
        }
    }

    private static async ValueTask<bool> TryAdvanceVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ClusterIncarnationId cluster,
        MembershipViewId expectedVersion,
        CancellationToken cancellationToken)
    {
        if (expectedVersion.Value == long.MaxValue)
        {
            throw new InvalidOperationException("Membership table version is exhausted.");
        }

        await using var command = new NpgsqlCommand(
            "UPDATE lakona_membership_cluster SET version = version + 1 WHERE singleton AND incarnation = $1 AND version = $2;",
            connection,
            transaction);
        command.Parameters.AddWithValue(cluster.Value);
        command.Parameters.AddWithValue(expectedVersion.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private static void AddEntryParameters(NpgsqlCommand command, MembershipTableEntry entry)
    {
        command.Parameters.AddWithValue(entry.Reference.Node.Value);
        command.Parameters.AddWithValue(entry.Reference.Incarnation.Value);
        command.Parameters.AddWithValue((short)entry.Status);
        command.Parameters.AddWithValue(entry.Version);
        command.Parameters.AddWithValue(entry.IAmAliveTime);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(ToPayload(entry), JsonOptions));
        command.Parameters.AddWithValue(entry.Generation);
    }

    private static MembershipTableEntry DeserializeEntry(ClusterIncarnationId cluster, NpgsqlDataReader reader)
    {
        var payload = JsonSerializer.Deserialize<EntryPayload>(reader.GetString(5), JsonOptions)
            ?? throw new InvalidOperationException("Membership row payload is empty.");
        return new MembershipTableEntry(
            new NodeReference(cluster, new NodeId(reader.GetString(0)), new NodeIncarnationId(reader.GetGuid(1))),
            (MembershipTableStatus)reader.GetInt16(2),
            new NodeEndpoint(payload.Endpoint, payload.EndpointMetadata),
            reader.GetInt64(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            payload.Labels,
            payload.ActorHosts.Select(static value => new NodeActorHostDescriptor(value.Actor, value.PolicyHash, value.BuildTag, value.Metadata)).ToArray(),
            payload.StartupActors.Select(static value => new StartupActorDescriptor(value.Actor, value.PolicyHash, value.BuildTag, value.Metadata)).ToArray(),
            payload.SuspectVotes.Select(value => new MembershipSuspectVote(
                new NodeReference(cluster, new NodeId(value.NodeId), new NodeIncarnationId(value.Incarnation)), value.Timestamp)).ToArray(),
            payload.StartTime,
            reader.GetInt64(6));
    }

    private static EntryPayload ToPayload(MembershipTableEntry entry) => new()
    {
        Endpoint = entry.ClusterEndpoint.Address,
        StartTime = entry.StartTime,
        EndpointMetadata = entry.ClusterEndpoint.Metadata,
        Labels = entry.Labels,
        ActorHosts = entry.ActorHosts.Select(static value => new ActorPayload(value.Actor, value.PolicyHash, value.BuildTag, value.Metadata)).ToArray(),
        StartupActors = entry.StartupActors.Select(static value => new ActorPayload(value.Actor, value.PolicyHash, value.BuildTag, value.Metadata)).ToArray(),
        SuspectVotes = entry.SuspectVotes.Select(static value => new VotePayload(value.Observer.Node.Value, value.Observer.Incarnation.Value, value.Timestamp)).ToArray()
    };

    public ValueTask DisposeAsync() => dataSource.DisposeAsync();

    private sealed class EntryPayload
    {
        public string Endpoint { get; set; } = "";
        public DateTimeOffset StartTime { get; set; }
        public IReadOnlyDictionary<string, string> EndpointMetadata { get; set; } = new Dictionary<string, string>();
        public IReadOnlyDictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();
        public IReadOnlyList<ActorPayload> ActorHosts { get; set; } = [];
        public IReadOnlyList<ActorPayload> StartupActors { get; set; } = [];
        public IReadOnlyList<VotePayload> SuspectVotes { get; set; } = [];
    }

    private sealed record ActorPayload(string Actor, string PolicyHash, string BuildTag, IReadOnlyDictionary<string, string> Metadata);
    private sealed record VotePayload(string NodeId, Guid Incarnation, DateTimeOffset Timestamp);
}
