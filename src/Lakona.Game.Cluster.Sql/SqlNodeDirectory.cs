using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Sql
{
    public sealed class SqlNodeDirectory : INodeDirectory
    {
        private const int RegisterMaxAttempts = 8;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly SqlNodeDirectoryOptions _options;

        public SqlNodeDirectory(SqlNodeDirectoryOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async ValueTask<NodeRegistrationResult> RegisterAsync(
            NodeRegistration registration,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            if (registration is null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            for (var attempt = 1; attempt <= RegisterMaxAttempts; attempt++)
            {
                await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await using var transaction = await connection.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken).ConfigureAwait(false);
                try
                {
                    var existingEpoch = await SelectEpochAsync(
                        connection,
                        transaction,
                        registration.ClusterName,
                        registration.NodeId,
                        cancellationToken).ConfigureAwait(false);
                    var epoch = existingEpoch.HasValue ? existingEpoch.Value + 1 : 1;
                    var record = new NodeRecord(
                        registration.ClusterName,
                        registration.NodeId,
                        epoch,
                        registration.Endpoints,
                        registration.Services,
                        registration.Labels,
                        registration.State,
                        registration.LeaseExpiresAt,
                        now);

                    if (existingEpoch.HasValue)
                    {
                        await UpdateRecordAsync(connection, transaction, record, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await InsertRecordAsync(connection, transaction, record, cancellationToken).ConfigureAwait(false);
                    }

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return new NodeRegistrationResult(NodeRegistrationStatus.Registered, record);
                }
                catch (DbException) when (attempt < RegisterMaxAttempts)
                {
                    await RollbackQuietlyAsync(transaction).ConfigureAwait(false);
                    await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException("SQL node registration retry loop exited unexpectedly.");
        }

        public async ValueTask<NodeHeartbeatStatus> HeartbeatAsync(
            string clusterName,
            NodeId node,
            long nodeEpoch,
            DateTimeOffset leaseExpiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            if (nodeEpoch < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nodeEpoch), "Node epoch cannot be negative.");
            }

            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE " + _options.TableName + " " +
                "SET lease_expires_at = @lease_expires_at, updated_at = @updated_at " +
                "WHERE cluster_name = @cluster_name AND node_id = @node_id " +
                "AND node_epoch = @node_epoch " +
                "AND state <> @dead_state " +
                "AND lease_expires_at > @now";
            AddParameter(command, "@lease_expires_at", ToUtcTicks(leaseExpiresAt));
            AddParameter(command, "@updated_at", ToUtcTicks(now));
            AddParameter(command, "@cluster_name", clusterName);
            AddParameter(command, "@node_id", node.Value);
            AddParameter(command, "@node_epoch", nodeEpoch);
            AddParameter(command, "@dead_state", (int)NodeState.Dead);
            AddParameter(command, "@now", ToUtcTicks(now));
            var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected == 1)
            {
                return NodeHeartbeatStatus.Refreshed;
            }

            var status = await GetCurrentStatusAsync(
                connection,
                null,
                clusterName,
                node,
                nodeEpoch,
                now,
                cancellationToken).ConfigureAwait(false);
            return ToHeartbeatStatus(status);
        }

        public async ValueTask<NodeStateUpdateStatus> UpdateStateAsync(
            string clusterName,
            NodeId node,
            long nodeEpoch,
            NodeState state,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            if (nodeEpoch < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nodeEpoch), "Node epoch cannot be negative.");
            }

            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE " + _options.TableName + " " +
                "SET state = @state, updated_at = @updated_at " +
                "WHERE cluster_name = @cluster_name AND node_id = @node_id " +
                "AND node_epoch = @node_epoch " +
                "AND state <> @dead_state " +
                "AND lease_expires_at > @now";
            AddParameter(command, "@state", (int)state);
            AddParameter(command, "@updated_at", ToUtcTicks(now));
            AddParameter(command, "@cluster_name", clusterName);
            AddParameter(command, "@node_id", node.Value);
            AddParameter(command, "@node_epoch", nodeEpoch);
            AddParameter(command, "@dead_state", (int)NodeState.Dead);
            AddParameter(command, "@now", ToUtcTicks(now));
            var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected == 1)
            {
                return NodeStateUpdateStatus.Updated;
            }

            var status = await GetCurrentStatusAsync(
                connection,
                null,
                clusterName,
                node,
                nodeEpoch,
                now,
                cancellationToken).ConfigureAwait(false);
            return ToStateUpdateStatus(status);
        }

        public async ValueTask<NodeRecord?> ResolveAsync(
            string clusterName,
            NodeId node,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var record = await SelectRecordAsync(
                connection,
                null,
                clusterName,
                node,
                cancellationToken).ConfigureAwait(false);

            if (record is null || record.State == NodeState.Dead || record.IsExpired(now))
            {
                return null;
            }

            return record;
        }

        public async ValueTask<IReadOnlyList<NodeRecord>> QueryAsync(
            NodeDirectoryQuery query,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            if (query is null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT cluster_name, node_id, node_epoch, state, endpoints_json, services_json, labels_json, lease_expires_at, updated_at " +
                "FROM " + _options.TableName + " WHERE cluster_name = @cluster_name";
            AddParameter(command, "@cluster_name", query.ClusterName);

            var records = new List<NodeRecord>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var record = ReadRecord(reader);
                if (MatchesQuery(record, query, now))
                {
                    records.Add(record);
                }
            }

            return records
                .OrderBy(record => record.NodeId.Value, StringComparer.Ordinal)
                .ToArray();
        }

        public async ValueTask<int> ExpireAsync(
            string clusterName,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE " + _options.TableName + " " +
                "SET state = @dead_state, updated_at = @updated_at " +
                "WHERE cluster_name = @cluster_name " +
                "AND state <> @dead_state " +
                "AND lease_expires_at <= @now";
            AddParameter(command, "@dead_state", (int)NodeState.Dead);
            AddParameter(command, "@updated_at", ToUtcTicks(now));
            AddParameter(command, "@cluster_name", clusterName);
            AddParameter(command, "@now", ToUtcTicks(now));
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var connection = await _options.ConnectionFactory().ConfigureAwait(false);
            if (connection is null)
            {
                throw new InvalidOperationException("SQL node directory connection factory returned null.");
            }

            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            await ConfigureConnectionAsync(connection, cancellationToken).ConfigureAwait(false);

            return connection;
        }

        private async ValueTask ConfigureConnectionAsync(DbConnection connection, CancellationToken cancellationToken)
        {
            if (_options.Dialect != SqlNodeDirectoryDialect.Sqlite)
            {
                return;
            }

            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA busy_timeout = 5000";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<long?> SelectEpochAsync(
            DbConnection connection,
            DbTransaction transaction,
            string clusterName,
            NodeId node,
            CancellationToken cancellationToken)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "SELECT node_epoch FROM " + _options.TableName + " " +
                "WHERE cluster_name = @cluster_name AND node_id = @node_id";
            AddParameter(command, "@cluster_name", clusterName);
            AddParameter(command, "@node_id", node.Value);
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is null || result == DBNull.Value ? null : Convert.ToInt64(result);
        }

        private async ValueTask<NodeRecord?> SelectRecordAsync(
            DbConnection connection,
            DbTransaction? transaction,
            string clusterName,
            NodeId node,
            CancellationToken cancellationToken)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "SELECT cluster_name, node_id, node_epoch, state, endpoints_json, services_json, labels_json, lease_expires_at, updated_at " +
                "FROM " + _options.TableName + " " +
                "WHERE cluster_name = @cluster_name AND node_id = @node_id";
            AddParameter(command, "@cluster_name", clusterName);
            AddParameter(command, "@node_id", node.Value);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return ReadRecord(reader);
        }

        private async ValueTask<NodeAccessStatus> GetCurrentStatusAsync(
            DbConnection connection,
            DbTransaction? transaction,
            string clusterName,
            NodeId node,
            long nodeEpoch,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            var record = await SelectRecordAsync(
                connection,
                transaction,
                clusterName,
                node,
                cancellationToken).ConfigureAwait(false);
            if (record is null)
            {
                return NodeAccessStatus.NotFound;
            }

            if (record.NodeEpoch != nodeEpoch)
            {
                return NodeAccessStatus.EpochMismatch;
            }

            if (record.State == NodeState.Dead || record.IsExpired(now))
            {
                return NodeAccessStatus.Expired;
            }

            return NodeAccessStatus.Current;
        }

        private async ValueTask InsertRecordAsync(
            DbConnection connection,
            DbTransaction transaction,
            NodeRecord record,
            CancellationToken cancellationToken)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "INSERT INTO " + _options.TableName + " " +
                "(cluster_name, node_id, node_epoch, state, endpoints_json, services_json, labels_json, lease_expires_at, updated_at) " +
                "VALUES (@cluster_name, @node_id, @node_epoch, @state, @endpoints_json, @services_json, @labels_json, @lease_expires_at, @updated_at)";
            AddRecordParameters(command, record);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask UpdateRecordAsync(
            DbConnection connection,
            DbTransaction transaction,
            NodeRecord record,
            CancellationToken cancellationToken)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "UPDATE " + _options.TableName + " " +
                "SET node_epoch = @node_epoch, state = @state, endpoints_json = @endpoints_json, " +
                "services_json = @services_json, labels_json = @labels_json, lease_expires_at = @lease_expires_at, updated_at = @updated_at " +
                "WHERE cluster_name = @cluster_name AND node_id = @node_id";
            AddRecordParameters(command, record);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private static void AddRecordParameters(DbCommand command, NodeRecord record)
        {
            AddParameter(command, "@cluster_name", record.ClusterName);
            AddParameter(command, "@node_id", record.NodeId.Value);
            AddParameter(command, "@node_epoch", record.NodeEpoch);
            AddParameter(command, "@state", (int)record.State);
            AddParameter(command, "@endpoints_json", SerializeEndpoints(record.Endpoints));
            AddParameter(command, "@services_json", SerializeServices(record.Services));
            AddParameter(command, "@labels_json", SerializeStringDictionary(record.Labels));
            AddParameter(command, "@lease_expires_at", ToUtcTicks(record.LeaseExpiresAt));
            AddParameter(command, "@updated_at", ToUtcTicks(record.UpdatedAt));
        }

        private static void AddParameter(DbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        private static NodeRecord ReadRecord(DbDataReader reader)
        {
            return new NodeRecord(
                reader.GetString(0),
                new NodeId(reader.GetString(1)),
                reader.GetInt64(2),
                DeserializeEndpoints(reader.GetString(4)),
                DeserializeServices(reader.GetString(5)),
                DeserializeStringDictionary(reader.GetString(6)),
                (NodeState)reader.GetInt32(3),
                FromUtcTicks(reader.GetInt64(7)),
                FromUtcTicks(reader.GetInt64(8)));
        }

        private static bool MatchesQuery(NodeRecord record, NodeDirectoryQuery query, DateTimeOffset now)
        {
            if (!string.Equals(record.ClusterName, query.ClusterName, StringComparison.Ordinal))
            {
                return false;
            }

            if (!query.IncludeExpired && (record.State == NodeState.Dead || record.IsExpired(now)))
            {
                return false;
            }

            if (query.ServiceKind is not null && !record.HasService(query.ServiceKind, query.ServiceName))
            {
                return false;
            }

            if (query.ServiceKind is null && query.ServiceName is not null
                && !record.Services.Any(service => string.Equals(service.Name, query.ServiceName, StringComparison.Ordinal)))
            {
                return false;
            }

            if (query.State is not null && record.State != query.State.Value)
            {
                return false;
            }

            foreach (var label in query.Labels)
            {
                if (!record.Labels.TryGetValue(label.Key, out var value)
                    || !string.Equals(value, label.Value, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static string SerializeEndpoints(IReadOnlyDictionary<string, NodeEndpoint> endpoints)
        {
            var dto = endpoints.ToDictionary(
                pair => pair.Key,
                pair => new EndpointDto(pair.Value.Address, pair.Value.Metadata),
                StringComparer.Ordinal);
            return JsonSerializer.Serialize(dto, JsonOptions);
        }

        private static IReadOnlyDictionary<string, NodeEndpoint> DeserializeEndpoints(string json)
        {
            var dto = JsonSerializer.Deserialize<Dictionary<string, EndpointDto>>(json, JsonOptions)
                ?? new Dictionary<string, EndpointDto>(StringComparer.Ordinal);
            var endpoints = new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal);
            foreach (var pair in dto)
            {
                endpoints[pair.Key] = new NodeEndpoint(
                    pair.Value.Address,
                    ToOrdinalDictionary(pair.Value.Metadata));
            }

            return new ReadOnlyDictionary<string, NodeEndpoint>(endpoints);
        }

        private static string SerializeServices(IReadOnlyList<NodeServiceDescriptor> services)
        {
            var dto = services
                .Select(service => new ServiceDto(service.Kind, service.Name, service.Metadata))
                .ToArray();
            return JsonSerializer.Serialize(dto, JsonOptions);
        }

        private static IReadOnlyList<NodeServiceDescriptor> DeserializeServices(string json)
        {
            var dto = JsonSerializer.Deserialize<ServiceDto[]>(json, JsonOptions) ?? Array.Empty<ServiceDto>();
            var services = new List<NodeServiceDescriptor>(dto.Length);
            foreach (var service in dto)
            {
                services.Add(new NodeServiceDescriptor(
                    service.Kind,
                    service.Name,
                    ToOrdinalDictionary(service.Metadata)));
            }

            return new ReadOnlyCollection<NodeServiceDescriptor>(services);
        }

        private static string SerializeStringDictionary(IReadOnlyDictionary<string, string> values)
        {
            return JsonSerializer.Serialize(ToOrdinalDictionary(values), JsonOptions);
        }

        private static IReadOnlyDictionary<string, string> DeserializeStringDictionary(string json)
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            return new ReadOnlyDictionary<string, string>(ToOrdinalDictionary(values));
        }

        private static Dictionary<string, string> ToOrdinalDictionary(IReadOnlyDictionary<string, string>? values)
        {
            var copy = new Dictionary<string, string>(StringComparer.Ordinal);
            if (values is null)
            {
                return copy;
            }

            foreach (var pair in values)
            {
                copy[pair.Key] = pair.Value;
            }

            return copy;
        }

        private static long ToUtcTicks(DateTimeOffset value)
        {
            return value.UtcTicks;
        }

        private static DateTimeOffset FromUtcTicks(long value)
        {
            return new DateTimeOffset(value, TimeSpan.Zero);
        }

        private static async ValueTask RollbackQuietlyAsync(DbTransaction transaction)
        {
            try
            {
                await transaction.RollbackAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }
            catch (DbException)
            {
            }
        }

        private static Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken)
        {
            return Task.Delay(TimeSpan.FromMilliseconds(Math.Min(50, attempt * 5)), cancellationToken);
        }

        private static NodeHeartbeatStatus ToHeartbeatStatus(NodeAccessStatus status)
        {
            switch (status)
            {
                case NodeAccessStatus.NotFound:
                    return NodeHeartbeatStatus.NodeNotFound;
                case NodeAccessStatus.EpochMismatch:
                    return NodeHeartbeatStatus.EpochMismatch;
                case NodeAccessStatus.Expired:
                    return NodeHeartbeatStatus.Expired;
                default:
                    return NodeHeartbeatStatus.Refreshed;
            }
        }

        private static NodeStateUpdateStatus ToStateUpdateStatus(NodeAccessStatus status)
        {
            switch (status)
            {
                case NodeAccessStatus.NotFound:
                    return NodeStateUpdateStatus.NodeNotFound;
                case NodeAccessStatus.EpochMismatch:
                    return NodeStateUpdateStatus.EpochMismatch;
                case NodeAccessStatus.Expired:
                    return NodeStateUpdateStatus.Expired;
                default:
                    return NodeStateUpdateStatus.Updated;
            }
        }

        private enum NodeAccessStatus
        {
            Current,
            NotFound,
            EpochMismatch,
            Expired
        }

        private sealed class EndpointDto
        {
            public EndpointDto()
            {
                Address = string.Empty;
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            public EndpointDto(string address, IReadOnlyDictionary<string, string> metadata)
            {
                Address = address;
                Metadata = ToOrdinalDictionary(metadata);
            }

            public string Address { get; set; }

            public Dictionary<string, string> Metadata { get; set; }
        }

        private sealed class ServiceDto
        {
            public ServiceDto()
            {
                Kind = string.Empty;
                Name = string.Empty;
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            public ServiceDto(string kind, string name, IReadOnlyDictionary<string, string> metadata)
            {
                Kind = kind;
                Name = name;
                Metadata = ToOrdinalDictionary(metadata);
            }

            public string Kind { get; set; }

            public string Name { get; set; }

            public Dictionary<string, string> Metadata { get; set; }
        }
    }
}
