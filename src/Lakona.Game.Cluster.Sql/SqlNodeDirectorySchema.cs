using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster.Sql
{
    public static class SqlNodeDirectorySchema
    {
        public static async ValueTask EnsureCreatedAsync(
            DbConnection connection,
            SqlNodeDirectoryDialect dialect,
            string tableName = SqlNodeDirectoryOptions.DefaultTableName,
            CancellationToken cancellationToken = default)
        {
            if (connection is null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            var validatedTableName = ValidateTableName(tableName);
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            using var command = connection.CreateCommand();
            command.CommandText = CreateTableSql(dialect, validatedTableName);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        internal static string ValidateTableName(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
            {
                throw new ArgumentException("Table name is required.", nameof(tableName));
            }

            for (var i = 0; i < tableName.Length; i++)
            {
                var c = tableName[i];
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
                {
                    throw new ArgumentException("Table name can contain only letters, digits, and underscores.", nameof(tableName));
                }
            }

            return tableName;
        }

        private static string CreateTableSql(SqlNodeDirectoryDialect dialect, string tableName)
        {
            switch (dialect)
            {
                case SqlNodeDirectoryDialect.Sqlite:
                    return
                        "CREATE TABLE IF NOT EXISTS " + tableName + " (" +
                        "cluster_name TEXT NOT NULL, " +
                        "node_id TEXT NOT NULL, " +
                        "node_epoch INTEGER NOT NULL, " +
                        "state INTEGER NOT NULL, " +
                        "endpoints_json TEXT NOT NULL, " +
                        "services_json TEXT NOT NULL, " +
                        "labels_json TEXT NOT NULL, " +
                        "lease_expires_at INTEGER NOT NULL, " +
                        "updated_at INTEGER NOT NULL, " +
                        "PRIMARY KEY (cluster_name, node_id))";
                case SqlNodeDirectoryDialect.Postgres:
                    return
                        "CREATE TABLE IF NOT EXISTS " + tableName + " (" +
                        "cluster_name TEXT NOT NULL, " +
                        "node_id TEXT NOT NULL, " +
                        "node_epoch BIGINT NOT NULL, " +
                        "state INTEGER NOT NULL, " +
                        "endpoints_json TEXT NOT NULL, " +
                        "services_json TEXT NOT NULL, " +
                        "labels_json TEXT NOT NULL, " +
                        "lease_expires_at BIGINT NOT NULL, " +
                        "updated_at BIGINT NOT NULL, " +
                        "PRIMARY KEY (cluster_name, node_id))";
                case SqlNodeDirectoryDialect.MySql:
                    return
                        "CREATE TABLE IF NOT EXISTS " + tableName + " (" +
                        "cluster_name VARCHAR(256) NOT NULL, " +
                        "node_id VARCHAR(256) NOT NULL, " +
                        "node_epoch BIGINT NOT NULL, " +
                        "state INT NOT NULL, " +
                        "endpoints_json TEXT NOT NULL, " +
                        "services_json TEXT NOT NULL, " +
                        "labels_json TEXT NOT NULL, " +
                        "lease_expires_at BIGINT NOT NULL, " +
                        "updated_at BIGINT NOT NULL, " +
                        "PRIMARY KEY (cluster_name, node_id))";
                default:
                    throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "Unsupported SQL node directory dialect.");
            }
        }
    }
}
