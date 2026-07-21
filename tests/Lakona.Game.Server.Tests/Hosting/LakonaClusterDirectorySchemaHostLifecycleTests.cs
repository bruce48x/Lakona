using System.Data.Common;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Sql;
using Lakona.Game.Server.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class LakonaClusterDirectorySchemaHostLifecycleTests
{
    private const string SuccessMessage =
        "Lakona server started successfully. NodeId=data-1.";

    [Fact]
    public async Task Enabled_bootstrap_upgrades_the_directory_before_node_registration()
    {
        var connectionString = $"Data Source=lakona-schema-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync(TestContext.Current.CancellationToken);
        await CreateLegacyDirectoryTableAsync(keeper);
        var events = new List<string>();

        using var host = CreateHost(
            ensureSchemaOnStartup: true,
            services =>
            {
                services.RemoveAll<SqlNodeDirectoryOptions>();
                services.AddSingleton(new SqlNodeDirectoryOptions(
                    () => new ValueTask<DbConnection>(new SqliteConnection(connectionString)),
                    SqlNodeDirectoryDialect.Sqlite));
                services.RemoveAll<INodeDirectory>();
                services.AddSingleton<SqlNodeDirectory>();
                services.AddSingleton<INodeDirectory>(provider => new RecordingNodeDirectory(
                    provider.GetRequiredService<SqlNodeDirectory>(),
                    async cancellationToken =>
                    {
                        await AssertCurrentSchemaAsync(keeper, cancellationToken);
                        events.Add("schema-ready");
                    },
                    () => events.Add("node-registration")));
            });

        await host.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.Equal(["schema-ready", "node-registration"], events);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Disabled_bootstrap_does_not_open_a_schema_connection()
    {
        var connectionFactoryCalls = 0;
        var directory = new RecordingNodeDirectory(new InMemoryNodeDirectory());
        using var host = CreateHost(
            ensureSchemaOnStartup: false,
            services =>
            {
                services.RemoveAll<SqlNodeDirectoryOptions>();
                services.AddSingleton(new SqlNodeDirectoryOptions(
                    () =>
                    {
                        connectionFactoryCalls++;
                        throw new InvalidOperationException("schema connection must remain unopened");
                    },
                    SqlNodeDirectoryDialect.Sqlite));
                services.RemoveAll<INodeDirectory>();
                services.AddSingleton<INodeDirectory>(directory);
            });

        await host.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.Equal(0, connectionFactoryCalls);
            Assert.Equal(1, directory.RegistrationCalls);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Bootstrap_failure_prevents_node_registration_and_success_reporting()
    {
        var directory = new RecordingNodeDirectory(new InMemoryNodeDirectory());
        using var loggerProvider = new MessageRecordingLoggerProvider();
        using var host = CreateHost(
            ensureSchemaOnStartup: true,
            services =>
            {
                services.RemoveAll<SqlNodeDirectoryOptions>();
                services.AddSingleton(new SqlNodeDirectoryOptions(
                    () => throw new InvalidOperationException("schema bootstrap failed"),
                    SqlNodeDirectoryDialect.Sqlite));
                services.RemoveAll<INodeDirectory>();
                services.AddSingleton<INodeDirectory>(directory);
            },
            loggerProvider);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal("schema bootstrap failed", exception.Message);
        Assert.Equal(0, directory.RegistrationCalls);
        Assert.DoesNotContain(SuccessMessage, loggerProvider.Messages);
    }

    private static IHost CreateHost(
        bool ensureSchemaOnStartup,
        Action<IServiceCollection> configureServices,
        ILoggerProvider? loggerProvider = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Node:Id"] = "data-1",
                ["Lakona:Cluster:Endpoint"] = "tcp://127.0.0.1:21001",
                ["Lakona:Cluster:Directory:Provider"] = "postgres",
                ["Lakona:Cluster:Directory:ConnectionStringName"] = "LakonaClusterPostgres",
                ["Lakona:Cluster:Directory:EnsureSchemaOnStartup"] = ensureSchemaOnStartup.ToString(),
                ["ConnectionStrings:LakonaClusterPostgres"] = "Host=unused;Database=unused"
            })
            .Build();

        return Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                if (loggerProvider is not null)
                {
                    logging.AddProvider(loggerProvider);
                }
            })
            .ConfigureServices(services =>
            {
                services.AddLakonaGameServer(configuration);
                configureServices(services);
            })
            .Build();
    }

    private static async Task CreateLegacyDirectoryTableAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE lakona_cluster_nodes (" +
            "cluster_name TEXT NOT NULL, node_id TEXT NOT NULL, node_epoch INTEGER NOT NULL, " +
            "state INTEGER NOT NULL, endpoints_json TEXT NOT NULL, actor_hosts_json TEXT NOT NULL, " +
            "labels_json TEXT NOT NULL, lease_expires_at INTEGER NOT NULL, updated_at INTEGER NOT NULL, " +
            "PRIMARY KEY (cluster_name, node_id))";
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task AssertCurrentSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT startup_actors_json FROM lakona_cluster_nodes WHERE 1 = 0";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.NotNull(reader);
    }

    private sealed class RecordingNodeDirectory(
        INodeDirectory inner,
        Func<CancellationToken, Task>? beforeRegistration = null,
        Action? onRegistration = null) : INodeDirectory
    {
        public int RegistrationCalls { get; private set; }

        public async ValueTask<NodeRegistrationResult> RegisterAsync(
            NodeRegistration registration,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            if (beforeRegistration is not null)
            {
                await beforeRegistration(cancellationToken);
            }

            RegistrationCalls++;
            onRegistration?.Invoke();
            return await inner.RegisterAsync(registration, now, cancellationToken);
        }

        public ValueTask<NodeHeartbeatStatus> HeartbeatAsync(
            string clusterName,
            NodeId node,
            long nodeEpoch,
            DateTimeOffset leaseExpiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            inner.HeartbeatAsync(clusterName, node, nodeEpoch, leaseExpiresAt, now, cancellationToken);

        public ValueTask<NodeStateUpdateStatus> UpdateStateAsync(
            string clusterName,
            NodeId node,
            long nodeEpoch,
            NodeState state,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            inner.UpdateStateAsync(clusterName, node, nodeEpoch, state, now, cancellationToken);

        public ValueTask<NodeRecord?> ResolveAsync(
            string clusterName,
            NodeId node,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            inner.ResolveAsync(clusterName, node, now, cancellationToken);

        public ValueTask<IReadOnlyList<NodeRecord>> QueryAsync(
            NodeDirectoryQuery query,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, now, cancellationToken);

        public ValueTask<int> ExpireAsync(
            string clusterName,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            inner.ExpireAsync(clusterName, now, cancellationToken);
    }

    private sealed class MessageRecordingLoggerProvider : ILoggerProvider
    {
        private readonly object _gate = new();

        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(MessageRecordingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (owner._gate)
                {
                    owner.Messages.Add(formatter(state, exception));
                }
            }
        }
    }
}
