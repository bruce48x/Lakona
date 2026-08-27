using System.Data;
using System.Text.Json;
using MySqlConnector;

namespace Lakona.Game.Cluster.Membership;

internal sealed class MySqlMembershipTable : IMembershipTable, IAsyncDisposable
{
    private const string SchemaMarker = "lakona-membership-schema:1";
    private const string ValidateSchemaSql = """
        SELECT singleton, incarnation, build_tag, version, next_generation
        FROM lakona_membership_cluster
        WHERE 1 = 0;

        SELECT node_id, node_incarnation, generation, status, entry_version,
               i_am_alive, payload
        FROM lakona_membership_member
        WHERE 1 = 0;
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly MySqlDataSource dataSource;
    private readonly SemaphoreSlim schemaGate = new(1, 1);
    private volatile bool schemaValidated;

    public MySqlMembershipTable(MySqlDataSource dataSource) =>
        this.dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    public async ValueTask<MembershipTableGeneration> AllocateGenerationAsync(
        string buildTag,
        CancellationToken cancellationToken = default)
    {
        buildTag = new ClusterBuildTag(buildTag).Value;
        await ValidateSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await EnsureMetadataAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        string? clusterBuildTag;
        await using (var metadata = new MySqlCommand(
            "SELECT build_tag FROM lakona_membership_cluster WHERE singleton = 1 FOR UPDATE;",
            connection,
            transaction))
        {
            var value = await metadata.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            clusterBuildTag = value is null or DBNull ? null : (string)value;
        }

        if (clusterBuildTag is null)
        {
            await using var liveMembers = new MySqlCommand(
                "SELECT EXISTS (SELECT 1 FROM lakona_membership_member WHERE status <> 3);",
                connection,
                transaction);
            if (Convert.ToInt32(await liveMembers.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0)
            {
                throw new ClusterMembershipFencedException(
                    $"Membership metadata has no BuildTag while live members exist; node BuildTag '{buildTag}' cannot claim this cluster.");
            }

            await using var establish = new MySqlCommand(
                "UPDATE lakona_membership_cluster SET build_tag = @buildTag WHERE singleton = 1;",
                connection,
                transaction);
            establish.Parameters.AddWithValue("@buildTag", buildTag);
            await establish.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (!string.Equals(clusterBuildTag, buildTag, StringComparison.Ordinal))
        {
            throw new ClusterMembershipFencedException(
                $"Node BuildTag '{buildTag}' cannot join cluster BuildTag '{clusterBuildTag}'. " +
                "Deploy incompatible BuildTags to separate environments.");
        }

        long generation;
        Guid incarnation;
        await using (var read = new MySqlCommand(
            "SELECT incarnation, next_generation FROM lakona_membership_cluster WHERE singleton = 1;",
            connection,
            transaction))
        await using (var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Membership cluster metadata disappeared during generation allocation.");
            }
            incarnation = reader.GetGuid(0);
            generation = reader.GetInt64(1);
        }

        if (generation == long.MaxValue)
        {
            throw new InvalidOperationException("Membership generation is exhausted.");
        }

        await using (var advance = new MySqlCommand(
            "UPDATE lakona_membership_cluster SET next_generation = next_generation + 1 WHERE singleton = 1 AND next_generation = @generation;",
            connection,
            transaction))
        {
            advance.Parameters.AddWithValue("@generation", generation);
            if (await advance.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new InvalidOperationException("Membership generation changed inside its locked transaction.");
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new MembershipTableGeneration(
            new ClusterIncarnationId(incarnation),
            generation);
    }

    public async ValueTask<MembershipTableSnapshot> ReadOrCreateAsync(
        CancellationToken cancellationToken = default)
    {
        await ValidateSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken).ConfigureAwait(false);
        await EnsureMetadataAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        ClusterIncarnationId cluster;
        string? buildTag;
        MembershipViewId version;
        await using (var metadata = new MySqlCommand(
            "SELECT incarnation, build_tag, version FROM lakona_membership_cluster WHERE singleton = 1 FOR SHARE;",
            connection,
            transaction))
        await using (var reader = await metadata.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Membership cluster metadata disappeared during a transactional read.");
            }

            cluster = new ClusterIncarnationId(reader.GetGuid(0));
            buildTag = reader.IsDBNull(1) ? null : reader.GetString(1);
            version = new MembershipViewId(reader.GetInt64(2));
        }

        var entries = new List<MembershipTableEntry>();
        await using (var rows = new MySqlCommand(
            "SELECT node_id, node_incarnation, status, entry_version, i_am_alive, payload, generation FROM lakona_membership_member ORDER BY node_id, node_incarnation;",
            connection,
            transaction))
        await using (var reader = await rows.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                entries.Add(DeserializeEntry(cluster, reader));
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new MembershipTableSnapshot(cluster, buildTag, version, entries);
    }

    public async ValueTask<bool> TryInsertAsync(
        MembershipTableEntry entry,
        MembershipViewId expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Version != 1 || entry.Status != MembershipTableStatus.Joining) return false;

        await ValidateSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await TryAdvanceVersionAsync(
                    connection, transaction, entry.Reference.Cluster, expectedVersion, cancellationToken)
                .ConfigureAwait(false))
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            await using var command = new MySqlCommand(
                "INSERT INTO lakona_membership_member(node_id, node_incarnation, status, entry_version, i_am_alive, payload, generation) VALUES (@node, @incarnation, @status, @version, @alive, @payload, @generation);",
                connection,
                transaction);
            AddEntryParameters(command, entry);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (MySqlException exception) when (exception.Number == 1062)
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
        if (expectedEntryVersion == long.MaxValue || entry.Version != expectedEntryVersion + 1) return false;

        await ValidateSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (!await TryAdvanceVersionAsync(
                connection, transaction, entry.Reference.Cluster, expectedVersion, cancellationToken)
            .ConfigureAwait(false))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        await using var command = new MySqlCommand(
            """
            UPDATE lakona_membership_member
            SET status = @status, entry_version = @version, i_am_alive = @alive, payload = @payload
            WHERE node_id = @node AND node_incarnation = @incarnation
              AND generation = @generation AND entry_version = @expectedEntryVersion
              AND ((status = 0 AND @status IN (0, 1, 3))
                OR (status = 1 AND @status IN (1, 2, 3))
                OR (status = 2 AND @status IN (2, 3)));
            """,
            connection,
            transaction);
        AddEntryParameters(command, entry);
        command.Parameters.AddWithValue("@expectedEntryVersion", expectedEntryVersion);
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

        await ValidateSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await TryAdvanceVersionAsync(
                    connection, transaction, replacement.Reference.Cluster, expectedVersion, cancellationToken)
                .ConfigureAwait(false))
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            await using (var fence = new MySqlCommand(
                "UPDATE lakona_membership_member SET status = 3, entry_version = entry_version + 1 WHERE node_id = @node AND node_incarnation = @incarnation AND entry_version = @version AND generation < @generation AND status <> 3;",
                connection,
                transaction))
            {
                fence.Parameters.AddWithValue("@node", previous.Node.Value);
                fence.Parameters.AddWithValue("@incarnation", previous.Incarnation.Value.ToString("D"));
                fence.Parameters.AddWithValue("@version", expectedPreviousVersion);
                fence.Parameters.AddWithValue("@generation", replacement.Generation);
                if (await fence.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return false;
                }
            }

            await using (var insert = new MySqlCommand(
                "INSERT INTO lakona_membership_member(node_id, node_incarnation, status, entry_version, i_am_alive, payload, generation) VALUES (@node, @incarnation, @status, @version, @alive, @payload, @generation);",
                connection,
                transaction))
            {
                AddEntryParameters(insert, replacement);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (MySqlException exception) when (exception.Number == 1062)
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
        await ValidateSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(
            "UPDATE lakona_membership_member SET i_am_alive = @alive WHERE node_id = @node AND node_incarnation = @incarnation AND status <> 3 AND i_am_alive < @alive;",
            connection);
        command.Parameters.AddWithValue("@node", reference.Node.Value);
        command.Parameters.AddWithValue("@incarnation", reference.Incarnation.Value.ToString("D"));
        command.Parameters.AddWithValue("@alive", UtcTicks(timestamp));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async ValueTask<int> CleanupDefunctAsync(
        DateTimeOffset before,
        int maximumRows,
        CancellationToken cancellationToken = default)
    {
        if (maximumRows <= 0) throw new ArgumentOutOfRangeException(nameof(maximumRows));
        await ValidateSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(
            "DELETE FROM lakona_membership_member WHERE status = 3 AND i_am_alive < @before ORDER BY i_am_alive LIMIT @maximumRows;",
            connection);
        command.Parameters.AddWithValue("@before", UtcTicks(before));
        command.Parameters.AddWithValue("@maximumRows", maximumRows);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ValidateSchemaAsync(CancellationToken cancellationToken)
    {
        if (schemaValidated) return;
        await schemaGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (schemaValidated) return;
            try
            {
                await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await using (var marker = new MySqlCommand(
                    "SELECT table_comment FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'lakona_membership_cluster';",
                    connection))
                {
                    var value = await marker.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                    if (!string.Equals(value as string, SchemaMarker, StringComparison.Ordinal))
                    {
                        throw new MembershipSchemaException(
                            "Lakona MySQL Membership schema marker is missing or incompatible. " +
                            $"Expected '{SchemaMarker}'. Stop every cluster node and apply " +
                            "database/mysql/membership.sql with a deployment account.",
                            new InvalidOperationException("Membership schema marker mismatch."));
                    }
                }

                await using (var command = new MySqlCommand(ValidateSchemaSql, connection))
                {
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                schemaValidated = true;
            }
            catch (MySqlException exception)
            {
                throw new MembershipSchemaException(
                    "Lakona MySQL Membership schema is not installed, incompatible, or inaccessible. " +
                    "Stop every cluster node, apply database/mysql/membership.sql with a deployment account, " +
                    "and grant the runtime account SELECT, INSERT, UPDATE, and DELETE on the Membership tables.",
                    exception);
            }
        }
        finally
        {
            schemaGate.Release();
        }
    }

    private static async ValueTask EnsureMetadataAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var create = new MySqlCommand(
            "INSERT IGNORE INTO lakona_membership_cluster(singleton, incarnation, build_tag, version, next_generation) VALUES (1, @incarnation, NULL, 0, 1);",
            connection,
            transaction);
        create.Parameters.AddWithValue("@incarnation", Guid.NewGuid().ToString("D"));
        await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<bool> TryAdvanceVersionAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        ClusterIncarnationId cluster,
        MembershipViewId expectedVersion,
        CancellationToken cancellationToken)
    {
        if (expectedVersion.Value == long.MaxValue)
        {
            throw new InvalidOperationException("Membership table version is exhausted.");
        }

        await using var command = new MySqlCommand(
            "UPDATE lakona_membership_cluster SET version = version + 1 WHERE singleton = 1 AND incarnation = @incarnation AND version = @version;",
            connection,
            transaction);
        command.Parameters.AddWithValue("@incarnation", cluster.Value.ToString("D"));
        command.Parameters.AddWithValue("@version", expectedVersion.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private static void AddEntryParameters(MySqlCommand command, MembershipTableEntry entry)
    {
        command.Parameters.AddWithValue("@node", entry.Reference.Node.Value);
        command.Parameters.AddWithValue("@incarnation", entry.Reference.Incarnation.Value.ToString("D"));
        command.Parameters.AddWithValue("@status", (short)entry.Status);
        command.Parameters.AddWithValue("@version", entry.Version);
        command.Parameters.AddWithValue("@alive", UtcTicks(entry.IAmAliveTime));
        command.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(ToPayload(entry), JsonOptions));
        command.Parameters.AddWithValue("@generation", entry.Generation);
    }

    private static MembershipTableEntry DeserializeEntry(ClusterIncarnationId cluster, MySqlDataReader reader)
    {
        var payload = JsonSerializer.Deserialize<EntryPayload>(reader.GetString(5), JsonOptions)
            ?? throw new InvalidOperationException("Membership row payload is empty.");
        return new MembershipTableEntry(
            new NodeReference(cluster, new NodeId(reader.GetString(0)), new NodeIncarnationId(reader.GetGuid(1))),
            (MembershipTableStatus)reader.GetInt16(2),
            new NodeEndpoint(payload.Endpoint, payload.EndpointMetadata),
            reader.GetInt64(3),
            new DateTimeOffset(reader.GetInt64(4), TimeSpan.Zero),
            payload.Labels,
            payload.ActorHosts.Select(static value => new NodeActorHostDescriptor(value.Actor, value.PolicyHash, value.HotfixVersion, value.Metadata)).ToArray(),
            payload.StartupActors.Select(static value => new StartupActorDescriptor(value.Actor, value.PolicyHash, value.HotfixVersion, value.Metadata)).ToArray(),
            payload.SuspectVotes.Select(value => new MembershipSuspectVote(
                new NodeReference(cluster, new NodeId(value.NodeId), new NodeIncarnationId(value.Incarnation)), value.Timestamp)).ToArray(),
            payload.StartTime,
            reader.GetInt64(6));
    }

    private static long UtcTicks(DateTimeOffset value) => value.UtcDateTime.Ticks;

    private static EntryPayload ToPayload(MembershipTableEntry entry) => new()
    {
        Endpoint = entry.ClusterEndpoint.Address,
        StartTime = entry.StartTime,
        EndpointMetadata = entry.ClusterEndpoint.Metadata,
        Labels = entry.Labels,
        ActorHosts = entry.ActorHosts.Select(static value => new ActorPayload(value.Actor, value.PolicyHash, value.HotfixVersion, value.Metadata)).ToArray(),
        StartupActors = entry.StartupActors.Select(static value => new ActorPayload(value.Actor, value.PolicyHash, value.HotfixVersion, value.Metadata)).ToArray(),
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

    private sealed record ActorPayload(string Actor, string PolicyHash, string HotfixVersion, IReadOnlyDictionary<string, string> Metadata);
    private sealed record VotePayload(string NodeId, Guid Incarnation, DateTimeOffset Timestamp);
}
