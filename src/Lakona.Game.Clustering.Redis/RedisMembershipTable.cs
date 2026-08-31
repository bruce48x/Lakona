using System.Globalization;
using System.Text;
using System.Text.Json;
using StackExchange.Redis;

namespace Lakona.Game.Cluster.Membership;

internal sealed class RedisMembershipTable : IMembershipTable, IAsyncDisposable
{
    private const string SchemaVersion = "1";
    private const string PayloadPrefix = "p|";
    private const string StatusPrefix = "s|";
    private const string VersionPrefix = "v|";
    private const string AlivePrefix = "a|";
    private const string GenerationPrefix = "g|";
    private const string LivePrefix = "l|";

    private const string InitializeScript = """
        local schema = redis.call('HGET', KEYS[1], 'schema')
        if not schema then
          if redis.call('HLEN', KEYS[1]) ~= 0 then return {0, ''} end
          redis.call('HSET', KEYS[1],
            'schema', '1', 'cluster', ARGV[1], 'version', '0', 'next_generation', '1')
          schema = '1'
        end
        if schema ~= '1' then return {0, schema} end
        return {1, redis.call('HGET', KEYS[1], 'cluster')}
        """;

    private const string AllocateGenerationScript = """
        local schema = redis.call('HGET', KEYS[1], 'schema')
        if not schema then
          if redis.call('HLEN', KEYS[1]) ~= 0 then return {0, ''} end
          redis.call('HSET', KEYS[1],
            'schema', '1', 'cluster', ARGV[1], 'version', '0', 'next_generation', '1')
          schema = '1'
        end
        if schema ~= '1' then return {0, schema} end
        local current = redis.call('HGET', KEYS[1], 'build_tag')
        if not current then
          local fields = redis.call('HKEYS', KEYS[1])
          for _, field in ipairs(fields) do
            if string.sub(field, 1, 2) == 'l|' then return {4, ''} end
          end
          redis.call('HSET', KEYS[1], 'build_tag', ARGV[2])
          current = ARGV[2]
        elseif current ~= ARGV[2] then
          return {2, current}
        end
        local generation = redis.call('HGET', KEYS[1], 'next_generation')
        if generation == '9223372036854775807' then return {3, ''} end
        redis.call('HINCRBY', KEYS[1], 'next_generation', 1)
        return {1, redis.call('HGET', KEYS[1], 'cluster'), generation}
        """;

    private const string InsertScript = """
        if redis.call('HGET', KEYS[1], 'schema') ~= '1' then return 0 end
        if redis.call('HGET', KEYS[1], 'cluster') ~= ARGV[1] then return 0 end
        if redis.call('HGET', KEYS[1], 'version') ~= ARGV[2] then return 0 end
        if redis.call('HEXISTS', KEYS[1], ARGV[4]) == 1 then return 0 end
        if redis.call('HEXISTS', KEYS[1], 'p|' .. ARGV[3]) == 1 then return 0 end
        redis.call('HSET', KEYS[1],
          'p|' .. ARGV[3], ARGV[5], 's|' .. ARGV[3], ARGV[6],
          'v|' .. ARGV[3], ARGV[7], 'a|' .. ARGV[3], ARGV[8],
          'g|' .. ARGV[3], ARGV[9], ARGV[4], ARGV[3])
        redis.call('HINCRBY', KEYS[1], 'version', 1)
        return 1
        """;

    private const string UpdateScript = """
        if redis.call('HGET', KEYS[1], 'schema') ~= '1' then return 0 end
        if redis.call('HGET', KEYS[1], 'cluster') ~= ARGV[1] then return 0 end
        if redis.call('HGET', KEYS[1], 'version') ~= ARGV[2] then return 0 end
        local oldstatus = redis.call('HGET', KEYS[1], 's|' .. ARGV[3])
        if not oldstatus then return 0 end
        if redis.call('HGET', KEYS[1], 'v|' .. ARGV[3]) ~= ARGV[4] then return 0 end
        if redis.call('HGET', KEYS[1], 'g|' .. ARGV[3]) ~= ARGV[10] then return 0 end
        local newstatus = ARGV[7]
        local allowed = (oldstatus == '0' and (newstatus == '0' or newstatus == '1' or newstatus == '3'))
          or (oldstatus == '1' and (newstatus == '1' or newstatus == '2' or newstatus == '3'))
          or (oldstatus == '2' and (newstatus == '2' or newstatus == '3'))
        if not allowed then return 0 end
        redis.call('HSET', KEYS[1],
          'p|' .. ARGV[3], ARGV[6], 's|' .. ARGV[3], newstatus,
          'v|' .. ARGV[3], ARGV[5], 'a|' .. ARGV[3], ARGV[8])
        if newstatus == '3' and redis.call('HGET', KEYS[1], ARGV[9]) == ARGV[3] then
          redis.call('HDEL', KEYS[1], ARGV[9])
        end
        redis.call('HINCRBY', KEYS[1], 'version', 1)
        return 1
        """;

    private const string ReplaceScript = """
        if redis.call('HGET', KEYS[1], 'schema') ~= '1' then return 0 end
        if redis.call('HGET', KEYS[1], 'cluster') ~= ARGV[1] then return 0 end
        if redis.call('HGET', KEYS[1], 'version') ~= ARGV[2] then return 0 end
        if redis.call('HGET', KEYS[1], 'v|' .. ARGV[3]) ~= ARGV[4] then return 0 end
        local oldstatus = redis.call('HGET', KEYS[1], 's|' .. ARGV[3])
        if not oldstatus or oldstatus == '3' then return 0 end
        local oldgeneration = redis.call('HGET', KEYS[1], 'g|' .. ARGV[3])
        if not oldgeneration then return 0 end
        if string.len(oldgeneration) > string.len(ARGV[11])
          or (string.len(oldgeneration) == string.len(ARGV[11]) and oldgeneration >= ARGV[11]) then
          return 0
        end
        if redis.call('HGET', KEYS[1], ARGV[5]) ~= ARGV[3] then return 0 end
        if redis.call('HEXISTS', KEYS[1], 'p|' .. ARGV[6]) == 1 then return 0 end
        redis.call('HSET', KEYS[1], 's|' .. ARGV[3], '3')
        redis.call('HINCRBY', KEYS[1], 'v|' .. ARGV[3], 1)
        redis.call('HSET', KEYS[1],
          'p|' .. ARGV[6], ARGV[7], 's|' .. ARGV[6], '0',
          'v|' .. ARGV[6], '1', 'a|' .. ARGV[6], ARGV[8],
          'g|' .. ARGV[6], ARGV[11], ARGV[5], ARGV[6])
        redis.call('HINCRBY', KEYS[1], 'version', 1)
        return 1
        """;

    private const string HeartbeatScript = """
        if redis.call('HGET', KEYS[1], 'schema') ~= '1' then return 0 end
        if redis.call('HGET', KEYS[1], 'cluster') ~= ARGV[1] then return 0 end
        local status = redis.call('HGET', KEYS[1], 's|' .. ARGV[2])
        if not status or status == '3' then return 0 end
        local current = redis.call('HGET', KEYS[1], 'a|' .. ARGV[2])
        if current and current >= ARGV[3] then return 0 end
        redis.call('HSET', KEYS[1], 'a|' .. ARGV[2], ARGV[3])
        return 1
        """;

    private const string CleanupScript = """
        if redis.call('HGET', KEYS[1], 'schema') ~= '1' then return -1 end
        local fields = redis.call('HKEYS', KEYS[1])
        local removed = 0
        for _, field in ipairs(fields) do
          if removed >= tonumber(ARGV[2]) then break end
          if string.sub(field, 1, 2) == 'p|' then
            local id = string.sub(field, 3)
            if redis.call('HGET', KEYS[1], 's|' .. id) == '3' then
              local alive = redis.call('HGET', KEYS[1], 'a|' .. id)
              if alive and alive < ARGV[1] then
                redis.call('HDEL', KEYS[1], field, 's|' .. id, 'v|' .. id, 'a|' .. id, 'g|' .. id)
                removed = removed + 1
              end
            end
          end
        end
        return removed
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private IConnectionMultiplexer? connection;
    private IDatabase? database;
    private readonly string? connectionString;
    private readonly SemaphoreSlim connectionGate = new(1, 1);
    private readonly RedisKey key;

    public RedisMembershipTable(IConnectionMultiplexer connection, string key)
    {
        this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        this.key = key;
        database = connection.GetDatabase();
    }

    public RedisMembershipTable(string connectionString, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        this.connectionString = connectionString;
        this.key = key;
    }

    public async ValueTask<MembershipTableGeneration> AllocateGenerationAsync(
        string buildTag,
        CancellationToken cancellationToken = default)
    {
        buildTag = new ClusterBuildTag(buildTag).Value;
        var candidate = Guid.NewGuid().ToString("N");
        var result = ResultArray(await EvaluateAsync(
            AllocateGenerationScript,
            [candidate, buildTag],
            cancellationToken).ConfigureAwait(false));
        var code = ResultInt(result[0]);
        if (code == 2)
        {
            throw new ClusterMembershipFencedException(
                $"Node BuildTag '{buildTag}' cannot join cluster BuildTag '{ResultString(result[1])}'. " +
                "Deploy incompatible BuildTags to separate environments.");
        }
        if (code == 3) throw new InvalidOperationException("Membership generation is exhausted.");
        if (code == 4)
        {
            throw new ClusterMembershipFencedException(
                $"Membership metadata has no BuildTag while live members exist; node BuildTag '{buildTag}' cannot claim this cluster.");
        }
        EnsureSchema(code, result.Length > 1 ? ResultString(result[1]) : null);
        return new MembershipTableGeneration(
            new ClusterIncarnationId(Guid.ParseExact(ResultString(result[1]), "N")),
            long.Parse(ResultString(result[2]), CultureInfo.InvariantCulture));
    }

    public async ValueTask<MembershipTableSnapshot> ReadOrCreateAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var database = await GetDatabaseAsync(cancellationToken).ConfigureAwait(false);
        var entries = await database.HashGetAllAsync(key).WaitAsync(cancellationToken).ConfigureAwait(false);
        var values = entries.ToDictionary(
            static entry => entry.Name.ToString(),
            static entry => entry.Value.ToString(),
            StringComparer.Ordinal);
        if (!values.TryGetValue("schema", out var schema) || schema != SchemaVersion)
        {
            throw SchemaException(schema);
        }

        var cluster = new ClusterIncarnationId(Guid.ParseExact(values["cluster"], "N"));
        var members = new List<MembershipTableEntry>();
        foreach (var pair in values)
        {
            if (!pair.Key.StartsWith(PayloadPrefix, StringComparison.Ordinal)) continue;
            var id = pair.Key[PayloadPrefix.Length..];
            members.Add(DeserializeEntry(
                cluster,
                pair.Value,
                ParseStatus(values[StatusPrefix + id]),
                long.Parse(values[VersionPrefix + id], CultureInfo.InvariantCulture),
                ParseTimestamp(values[AlivePrefix + id]),
                long.Parse(values[GenerationPrefix + id], CultureInfo.InvariantCulture)));
        }

        return new MembershipTableSnapshot(
            cluster,
            values.GetValueOrDefault("build_tag"),
            new MembershipViewId(long.Parse(values["version"], CultureInfo.InvariantCulture)),
            members);
    }

    public async ValueTask<bool> TryInsertAsync(
        MembershipTableEntry entry,
        MembershipViewId expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Version != 1 || entry.Status != MembershipTableStatus.Joining) return false;
        var id = MemberId(entry.Reference);
        return await EvaluateBooleanAsync(InsertScript,
            [Cluster(entry.Reference), Number(expectedVersion.Value), id, LiveField(entry.Reference.Node),
                SerializePayload(entry), Number((int)entry.Status), Number(entry.Version),
                Timestamp(entry.IAmAliveTime), Number(entry.Generation)], cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> TryUpdateAsync(
        MembershipTableEntry entry,
        long expectedEntryVersion,
        MembershipViewId expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (expectedVersion.Value == long.MaxValue) throw new InvalidOperationException("Membership table version is exhausted.");
        if (expectedEntryVersion == long.MaxValue || entry.Version != expectedEntryVersion + 1) return false;
        var id = MemberId(entry.Reference);
        return await EvaluateBooleanAsync(UpdateScript,
            [Cluster(entry.Reference), Number(expectedVersion.Value), id, Number(expectedEntryVersion),
                Number(entry.Version), SerializePayload(entry), Number((int)entry.Status),
                Timestamp(entry.IAmAliveTime), LiveField(entry.Reference.Node), Number(entry.Generation)],
            cancellationToken).ConfigureAwait(false);
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
        if (expectedVersion.Value == long.MaxValue) throw new InvalidOperationException("Membership table version is exhausted.");
        if (previous.Cluster != replacement.Reference.Cluster || previous.Node != replacement.Reference.Node
            || previous == replacement.Reference || expectedPreviousVersion == long.MaxValue
            || replacement.Version != 1 || replacement.Status != MembershipTableStatus.Joining)
        {
            return false;
        }

        return await EvaluateBooleanAsync(ReplaceScript,
            [Cluster(previous), Number(expectedVersion.Value), MemberId(previous), Number(expectedPreviousVersion),
                LiveField(previous.Node), MemberId(replacement.Reference), SerializePayload(replacement),
                Timestamp(replacement.IAmAliveTime), Number(replacement.Version), Number((int)replacement.Status),
                Number(replacement.Generation)], cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> TryUpdateIAmAliveAsync(
        NodeReference reference,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return await EvaluateBooleanAsync(HeartbeatScript,
            [Cluster(reference), MemberId(reference), Timestamp(timestamp)], cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<int> CleanupDefunctAsync(
        DateTimeOffset before,
        int maximumRows,
        CancellationToken cancellationToken = default)
    {
        if (maximumRows <= 0) throw new ArgumentOutOfRangeException(nameof(maximumRows));
        var result = await EvaluateAsync(CleanupScript,
            [Timestamp(before), Number(maximumRows)], cancellationToken).ConfigureAwait(false);
        var removed = ResultInt(result);
        if (removed < 0) throw SchemaException(null);
        return removed;
    }

    private async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        var result = ResultArray(await EvaluateAsync(
            InitializeScript,
            [Guid.NewGuid().ToString("N")],
            cancellationToken).ConfigureAwait(false));
        EnsureSchema(ResultInt(result[0]), result.Length > 1 ? ResultString(result[1]) : null);
    }

    private async ValueTask<bool> EvaluateBooleanAsync(
        string script,
        RedisValue[] arguments,
        CancellationToken cancellationToken) =>
        ResultInt(await EvaluateAsync(script, arguments, cancellationToken).ConfigureAwait(false)) == 1;

    private async ValueTask<RedisResult> EvaluateAsync(
        string script,
        RedisValue[] arguments,
        CancellationToken cancellationToken) =>
        await (await GetDatabaseAsync(cancellationToken).ConfigureAwait(false))
            .ScriptEvaluateAsync(script, [key], arguments).WaitAsync(cancellationToken).ConfigureAwait(false);

    private async ValueTask<IDatabase> GetDatabaseAsync(CancellationToken cancellationToken)
    {
        if (database is not null) return database;
        await connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (database is not null) return database;
            connection = await ConnectionMultiplexer.ConnectAsync(connectionString!)
                .WaitAsync(cancellationToken).ConfigureAwait(false);
            database = connection.GetDatabase();
            return database;
        }
        finally
        {
            connectionGate.Release();
        }
    }

    private static string MemberId(NodeReference reference) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(reference.Node.Value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_') + ":" + reference.Incarnation.Value.ToString("N");

    private static string LiveField(NodeId node) =>
        LivePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(node.Value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Cluster(NodeReference reference) => reference.Cluster.Value.ToString("N");
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Timestamp(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseTimestamp(string value) => DateTimeOffset.ParseExact(
        value, "O", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    private static MembershipTableStatus ParseStatus(string value) =>
        (MembershipTableStatus)int.Parse(value, CultureInfo.InvariantCulture);

    private static RedisResult[] ResultArray(RedisResult value) => (RedisResult[])value!;
    private static int ResultInt(RedisResult value) => (int)(long)value;
    private static string ResultString(RedisResult value) => (string)value!;

    internal static void EnsureSchema(int code, string? actual)
    {
        if (code != 1) throw SchemaException(actual);
    }

    private static MembershipSchemaException SchemaException(string? actual) => new(
        "Lakona Redis Membership data has an incompatible schema marker" +
        (string.IsNullOrEmpty(actual) ? "." : $" '{actual}'.") +
        " Stop every cluster node, delete the environment's Membership key, and start the cluster again.");

    private static string SerializePayload(MembershipTableEntry entry) =>
        JsonSerializer.Serialize(new EntryPayload
        {
            NodeId = entry.Reference.Node.Value,
            Incarnation = entry.Reference.Incarnation.Value,
            Endpoint = entry.ClusterEndpoint.Address,
            StartTime = entry.StartTime,
            EndpointMetadata = entry.ClusterEndpoint.Metadata,
            Labels = entry.Labels,
            ActorHosts = entry.ActorHosts.Select(static value => new ActorPayload(value.Actor, value.PolicyHash, value.HotfixVersion, value.Metadata)).ToArray(),
            StartupActors = entry.StartupActors.Select(static value => new ActorPayload(value.Actor, value.PolicyHash, value.HotfixVersion, value.Metadata)).ToArray(),
            SuspectVotes = entry.SuspectVotes.Select(static value => new VotePayload(value.Observer.Node.Value, value.Observer.Incarnation.Value, value.Timestamp)).ToArray()
        }, JsonOptions);

    private static MembershipTableEntry DeserializeEntry(
        ClusterIncarnationId cluster,
        string json,
        MembershipTableStatus status,
        long version,
        DateTimeOffset alive,
        long generation)
    {
        var payload = JsonSerializer.Deserialize<EntryPayload>(json, JsonOptions)
            ?? throw new InvalidOperationException("Redis Membership row payload is empty.");
        return new MembershipTableEntry(
            new NodeReference(cluster, new NodeId(payload.NodeId), new NodeIncarnationId(payload.Incarnation)),
            status,
            new NodeEndpoint(payload.Endpoint, payload.EndpointMetadata),
            version,
            alive,
            payload.Labels,
            payload.ActorHosts.Select(static value => new NodeActorHostDescriptor(value.Actor, value.PolicyHash, value.HotfixVersion, value.Metadata)).ToArray(),
            payload.StartupActors.Select(static value => new StartupActorDescriptor(value.Actor, value.PolicyHash, value.HotfixVersion, value.Metadata)).ToArray(),
            payload.SuspectVotes.Select(value => new MembershipSuspectVote(
                new NodeReference(cluster, new NodeId(value.NodeId), new NodeIncarnationId(value.Incarnation)), value.Timestamp)).ToArray(),
            payload.StartTime,
            generation);
    }

    public async ValueTask DisposeAsync()
    {
        if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
        connectionGate.Dispose();
    }

    private sealed class EntryPayload
    {
        public string NodeId { get; set; } = "";
        public Guid Incarnation { get; set; }
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
