using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Sql;
using Xunit;

namespace Lakona.Game.Cluster.Sql.Tests;

public sealed class SqlNodeDirectoryTests
{
    [Fact]
    public async Task RegisterPersistsAndIncrementsEpochAcrossDirectoryInstances()
    {
        await using var database = await OpenSharedDatabaseAsync();
        await SqlNodeDirectorySchema.EnsureCreatedAsync(
            database.KeeperConnection,
            SqlNodeDirectoryDialect.Sqlite,
            cancellationToken: TestContext.Current.CancellationToken);
        var now = DateTimeOffset.UtcNow;
        var first = CreateDirectory(database.ConnectionString);

        var firstResult = await first.RegisterAsync(
            TestRegistration("local", "node-a", now),
            now,
            TestContext.Current.CancellationToken);
        var second = CreateDirectory(database.ConnectionString);
        var secondResult = await second.RegisterAsync(
            TestRegistration("local", "node-a", now.AddSeconds(1)),
            now.AddSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(NodeRegistrationStatus.Registered, firstResult.Status);
        Assert.Equal(NodeRegistrationStatus.Registered, secondResult.Status);
        Assert.Equal(1, firstResult.Record!.NodeEpoch);
        Assert.Equal(2, secondResult.Record!.NodeEpoch);
    }

    [Fact]
    public async Task HeartbeatRejectsMismatchedEpoch()
    {
        await using var database = await OpenSharedDatabaseAsync();
        await SqlNodeDirectorySchema.EnsureCreatedAsync(
            database.KeeperConnection,
            SqlNodeDirectoryDialect.Sqlite,
            cancellationToken: TestContext.Current.CancellationToken);
        var directory = CreateDirectory(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        await directory.RegisterAsync(
            TestRegistration("local", "node-a", now),
            now,
            TestContext.Current.CancellationToken);
        await directory.RegisterAsync(
            TestRegistration("local", "node-a", now.AddSeconds(1)),
            now.AddSeconds(1),
            TestContext.Current.CancellationToken);

        var status = await directory.HeartbeatAsync(
            "local",
            "node-a",
            1,
            now.AddSeconds(40),
            now.AddSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Equal(NodeHeartbeatStatus.EpochMismatch, status);
    }

    [Fact]
    public async Task QueryFiltersByPersistedServiceKind()
    {
        await using var database = await OpenSharedDatabaseAsync();
        await SqlNodeDirectorySchema.EnsureCreatedAsync(
            database.KeeperConnection,
            SqlNodeDirectoryDialect.Sqlite,
            cancellationToken: TestContext.Current.CancellationToken);
        var directory = CreateDirectory(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        await directory.RegisterAsync(
            TestRegistration("local", "gateway-1", now, "gateway"),
            now,
            TestContext.Current.CancellationToken);
        await directory.RegisterAsync(
            TestRegistration("local", "room-1", now, "room"),
            now,
            TestContext.Current.CancellationToken);

        var rooms = await directory.QueryAsync(
            new NodeDirectoryQuery("local", serviceKind: "room"),
            now,
            TestContext.Current.CancellationToken);

        Assert.Single(rooms);
        Assert.Equal("room-1", rooms[0].NodeId.Value);
    }

    [Fact]
    public async Task UpdateStatePersistsAndResolveReturnsUpdatedRecord()
    {
        await using var database = await OpenSharedDatabaseAsync();
        await SqlNodeDirectorySchema.EnsureCreatedAsync(
            database.KeeperConnection,
            SqlNodeDirectoryDialect.Sqlite,
            cancellationToken: TestContext.Current.CancellationToken);
        var directory = CreateDirectory(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        var registered = await directory.RegisterAsync(
            TestRegistration("local", "node-a", now),
            now,
            TestContext.Current.CancellationToken);

        var status = await directory.UpdateStateAsync(
            "local",
            "node-a",
            registered.Record!.NodeEpoch,
            NodeState.Draining,
            now.AddSeconds(1),
            TestContext.Current.CancellationToken);
        var resolved = await directory.ResolveAsync(
            "local",
            "node-a",
            now.AddSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Equal(NodeStateUpdateStatus.Updated, status);
        Assert.NotNull(resolved);
        Assert.Equal(NodeState.Draining, resolved!.State);
    }

    [Fact]
    public async Task ResolveReturnsNullForDeadNode()
    {
        await using var database = await OpenSharedDatabaseAsync();
        await SqlNodeDirectorySchema.EnsureCreatedAsync(
            database.KeeperConnection,
            SqlNodeDirectoryDialect.Sqlite,
            cancellationToken: TestContext.Current.CancellationToken);
        var directory = CreateDirectory(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        var registered = await directory.RegisterAsync(
            TestRegistration("local", "node-a", now),
            now,
            TestContext.Current.CancellationToken);
        await directory.UpdateStateAsync(
            "local",
            "node-a",
            registered.Record!.NodeEpoch,
            NodeState.Dead,
            now.AddSeconds(1),
            TestContext.Current.CancellationToken);

        var resolved = await directory.ResolveAsync(
            "local",
            "node-a",
            now.AddSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ExpireMarksExpiredNodesDead()
    {
        await using var database = await OpenSharedDatabaseAsync();
        await SqlNodeDirectorySchema.EnsureCreatedAsync(
            database.KeeperConnection,
            SqlNodeDirectoryDialect.Sqlite,
            cancellationToken: TestContext.Current.CancellationToken);
        var directory = CreateDirectory(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        await directory.RegisterAsync(
            TestRegistration("local", "node-a", now),
            now,
            TestContext.Current.CancellationToken);

        var expired = await directory.ExpireAsync(
            "local",
            now.AddMinutes(1),
            TestContext.Current.CancellationToken);
        var records = await directory.QueryAsync(
            new NodeDirectoryQuery("local", state: NodeState.Dead, includeExpired: true),
            now.AddMinutes(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, expired);
        Assert.Single(records);
        Assert.Equal("node-a", records[0].NodeId.Value);
        Assert.Equal(NodeState.Dead, records[0].State);
    }

    [Fact]
    public async Task ResolvePreservesTimestampTicksAndExpiresAtExactTick()
    {
        await using var database = await OpenSharedDatabaseAsync();
        await SqlNodeDirectorySchema.EnsureCreatedAsync(
            database.KeeperConnection,
            SqlNodeDirectoryDialect.Sqlite,
            cancellationToken: TestContext.Current.CancellationToken);
        var directory = CreateDirectory(database.ConnectionString);
        var now = new DateTimeOffset(2026, 5, 28, 12, 0, 0, TimeSpan.Zero).AddTicks(1234);
        var leaseExpiresAt = now.AddSeconds(30).AddTicks(5678);

        var registered = await directory.RegisterAsync(
            TestRegistration("local", "node-a", now, leaseExpiresAt: leaseExpiresAt),
            now,
            TestContext.Current.CancellationToken);
        var resolvedBeforeExpiry = await directory.ResolveAsync(
            "local",
            "node-a",
            leaseExpiresAt.AddTicks(-1),
            TestContext.Current.CancellationToken);
        var resolvedAtExpiry = await directory.ResolveAsync(
            "local",
            "node-a",
            leaseExpiresAt,
            TestContext.Current.CancellationToken);

        Assert.NotEqual(0, now.Ticks % TimeSpan.TicksPerMillisecond);
        Assert.NotEqual(0, leaseExpiresAt.Ticks % TimeSpan.TicksPerMillisecond);
        Assert.NotNull(resolvedBeforeExpiry);
        Assert.Null(resolvedAtExpiry);
        Assert.Equal(leaseExpiresAt.UtcTicks, resolvedBeforeExpiry!.LeaseExpiresAt.UtcTicks);
        Assert.Equal(now.UtcTicks, resolvedBeforeExpiry.UpdatedAt.UtcTicks);
        Assert.Equal(now.UtcTicks, registered.Record!.UpdatedAt.UtcTicks);
    }

    [Fact]
    public async Task ConcurrentRegistrationsReturnUniqueEpochs()
    {
        await using var database = await OpenSharedDatabaseAsync();
        await SqlNodeDirectorySchema.EnsureCreatedAsync(
            database.KeeperConnection,
            SqlNodeDirectoryDialect.Sqlite,
            cancellationToken: TestContext.Current.CancellationToken);
        var now = DateTimeOffset.UtcNow;
        const int registrationCount = 12;

        var tasks = Enumerable.Range(0, registrationCount)
            .Select(i =>
            {
                var directory = CreateDirectory(database.ConnectionString);
                return directory.RegisterAsync(
                    TestRegistration("local", "node-a", now.AddMilliseconds(i)),
                    now.AddMilliseconds(i),
                    TestContext.Current.CancellationToken).AsTask();
            })
            .ToArray();

        var results = await Task.WhenAll(tasks);
        var epochs = results.Select(result => result.Record!.NodeEpoch).Order().ToArray();

        Assert.Equal(Enumerable.Range(1, registrationCount).Select(i => (long)i), epochs);
    }

    [Fact]
    public async Task HeartbeatWithStaleEpochDoesNotMutateCurrentEpochRow()
    {
        await using var database = await OpenSharedDatabaseAsync();
        await SqlNodeDirectorySchema.EnsureCreatedAsync(
            database.KeeperConnection,
            SqlNodeDirectoryDialect.Sqlite,
            cancellationToken: TestContext.Current.CancellationToken);
        var directory = CreateDirectory(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        await directory.RegisterAsync(
            TestRegistration("local", "node-a", now),
            now,
            TestContext.Current.CancellationToken);
        var current = await directory.RegisterAsync(
            TestRegistration("local", "node-a", now.AddSeconds(1)),
            now.AddSeconds(1),
            TestContext.Current.CancellationToken);
        var originalLease = current.Record!.LeaseExpiresAt;

        var status = await directory.HeartbeatAsync(
            "local",
            "node-a",
            1,
            now.AddMinutes(10),
            now.AddSeconds(2),
            TestContext.Current.CancellationToken);
        var resolved = await directory.ResolveAsync(
            "local",
            "node-a",
            now.AddSeconds(3),
            TestContext.Current.CancellationToken);

        Assert.Equal(NodeHeartbeatStatus.EpochMismatch, status);
        Assert.NotNull(resolved);
        Assert.Equal(2, resolved!.NodeEpoch);
        Assert.Equal(originalLease.UtcTicks, resolved.LeaseExpiresAt.UtcTicks);
    }

    [Fact]
    public async Task UpdateStateWithStaleEpochDoesNotMutateCurrentEpochRow()
    {
        await using var database = await OpenSharedDatabaseAsync();
        await SqlNodeDirectorySchema.EnsureCreatedAsync(
            database.KeeperConnection,
            SqlNodeDirectoryDialect.Sqlite,
            cancellationToken: TestContext.Current.CancellationToken);
        var directory = CreateDirectory(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        await directory.RegisterAsync(
            TestRegistration("local", "node-a", now),
            now,
            TestContext.Current.CancellationToken);
        var current = await directory.RegisterAsync(
            TestRegistration("local", "node-a", now.AddSeconds(1)),
            now.AddSeconds(1),
            TestContext.Current.CancellationToken);

        var status = await directory.UpdateStateAsync(
            "local",
            "node-a",
            1,
            NodeState.Draining,
            now.AddSeconds(2),
            TestContext.Current.CancellationToken);
        var resolved = await directory.ResolveAsync(
            "local",
            "node-a",
            now.AddSeconds(3),
            TestContext.Current.CancellationToken);

        Assert.Equal(NodeStateUpdateStatus.EpochMismatch, status);
        Assert.NotNull(resolved);
        Assert.Equal(current.Record!.NodeEpoch, resolved!.NodeEpoch);
        Assert.Equal(NodeState.Ready, resolved.State);
    }

    [Fact]
    public async Task HeartbeatAndUpdateStateReturnExpiredForDeadNode()
    {
        await using var database = await OpenSharedDatabaseAsync();
        await SqlNodeDirectorySchema.EnsureCreatedAsync(
            database.KeeperConnection,
            SqlNodeDirectoryDialect.Sqlite,
            cancellationToken: TestContext.Current.CancellationToken);
        var directory = CreateDirectory(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        var registered = await directory.RegisterAsync(
            TestRegistration("local", "node-a", now),
            now,
            TestContext.Current.CancellationToken);
        await directory.UpdateStateAsync(
            "local",
            "node-a",
            registered.Record!.NodeEpoch,
            NodeState.Dead,
            now.AddSeconds(1),
            TestContext.Current.CancellationToken);

        var heartbeat = await directory.HeartbeatAsync(
            "local",
            "node-a",
            registered.Record.NodeEpoch,
            now.AddMinutes(10),
            now.AddSeconds(2),
            TestContext.Current.CancellationToken);
        var update = await directory.UpdateStateAsync(
            "local",
            "node-a",
            registered.Record.NodeEpoch,
            NodeState.Ready,
            now.AddSeconds(3),
            TestContext.Current.CancellationToken);
        var deadRecords = await directory.QueryAsync(
            new NodeDirectoryQuery("local", state: NodeState.Dead, includeExpired: true),
            now.AddSeconds(4),
            TestContext.Current.CancellationToken);

        Assert.Equal(NodeHeartbeatStatus.Expired, heartbeat);
        Assert.Equal(NodeStateUpdateStatus.Expired, update);
        Assert.Single(deadRecords);
    }

    [Fact]
    public async Task OperationsDisposeFactoryConnections()
    {
        await using var database = await OpenSharedDatabaseAsync();
        await SqlNodeDirectorySchema.EnsureCreatedAsync(
            database.KeeperConnection,
            SqlNodeDirectoryDialect.Sqlite,
            cancellationToken: TestContext.Current.CancellationToken);
        var disposedCount = 0;
        var directory = new SqlNodeDirectory(
            new SqlNodeDirectoryOptions(
                () =>
                {
                    DbConnection connection = new CountingDbConnection(
                        new SqliteConnection(database.ConnectionString),
                        () => disposedCount++);
                    return ValueTask.FromResult(connection);
                },
                SqlNodeDirectoryDialect.Sqlite));
        var now = DateTimeOffset.UtcNow;

        await directory.RegisterAsync(
            TestRegistration("local", "node-a", now),
            now,
            TestContext.Current.CancellationToken);
        await directory.ResolveAsync(
            "local",
            "node-a",
            now.AddSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, disposedCount);
    }

    private static async Task<SharedSqliteDatabase> OpenSharedDatabaseAsync()
    {
        var connectionString = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return new SharedSqliteDatabase(connectionString, connection);
    }

    private static SqlNodeDirectory CreateDirectory(string connectionString)
    {
        return new SqlNodeDirectory(
            new SqlNodeDirectoryOptions(
                () => ValueTask.FromResult<DbConnection>(new SqliteConnection(connectionString)),
                SqlNodeDirectoryDialect.Sqlite));
    }

    private static NodeRegistration TestRegistration(
        string clusterName,
        string nodeId,
        DateTimeOffset now,
        string serviceKind = "gateway",
        DateTimeOffset? leaseExpiresAt = null)
    {
        return new NodeRegistration(
            clusterName,
            nodeId,
            new Dictionary<string, NodeEndpoint>
            {
                ["cluster"] = new NodeEndpoint(
                    $"tcp://127.0.0.1:{21000 + Math.Abs(nodeId.GetHashCode() % 1000)}",
                    new Dictionary<string, string>
                    {
                        ["transport"] = "tcp"
                    })
            },
            new[]
            {
                new NodeServiceDescriptor(
                    serviceKind,
                    metadata: new Dictionary<string, string>
                    {
                        ["role"] = serviceKind
                    })
            },
            leaseExpiresAt ?? now.AddSeconds(30),
            NodeState.Ready,
            new Dictionary<string, string>
            {
                ["zone"] = "local"
            });
    }

    private sealed class SharedSqliteDatabase : IAsyncDisposable
    {
        public SharedSqliteDatabase(string connectionString, SqliteConnection keeperConnection)
        {
            ConnectionString = connectionString;
            KeeperConnection = keeperConnection;
        }

        public string ConnectionString { get; }

        public SqliteConnection KeeperConnection { get; }

        public ValueTask DisposeAsync()
        {
            return KeeperConnection.DisposeAsync();
        }
    }

    private sealed class CountingDbConnection : DbConnection
    {
        private readonly DbConnection _inner;
        private readonly Action _onDispose;
        private bool _disposed;

        public CountingDbConnection(DbConnection inner, Action onDispose)
        {
            _inner = inner;
            _onDispose = onDispose;
        }

#pragma warning disable CS8765
        public override string ConnectionString
        {
            get => _inner.ConnectionString;
            [param: AllowNull]
            set => _inner.ConnectionString = value;
        }
#pragma warning restore CS8765

        public override string Database => _inner.Database;

        public override string DataSource => _inner.DataSource;

        public override string ServerVersion => _inner.ServerVersion;

        public override ConnectionState State => _inner.State;

        public override void ChangeDatabase(string databaseName)
        {
            _inner.ChangeDatabase(databaseName);
        }

        public override void Close()
        {
            _inner.Close();
        }

        public override void Open()
        {
            _inner.Open();
        }

        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            return _inner.OpenAsync(cancellationToken);
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            return _inner.BeginTransaction(isolationLevel);
        }

        protected override DbCommand CreateDbCommand()
        {
            return _inner.CreateCommand();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                CountDispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            CountDispose();
            await base.DisposeAsync();
        }

        private void CountDispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _onDispose();
        }
    }
}
