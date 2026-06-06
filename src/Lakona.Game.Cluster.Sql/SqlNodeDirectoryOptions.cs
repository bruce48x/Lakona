using System;
using System.Data.Common;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster.Sql
{
    public sealed class SqlNodeDirectoryOptions
    {
        public const string DefaultTableName = "lakona_game_cluster_nodes";

        public SqlNodeDirectoryOptions(
            Func<ValueTask<DbConnection>> connectionFactory,
            SqlNodeDirectoryDialect dialect,
            string tableName = DefaultTableName)
        {
            ConnectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            Dialect = dialect;
            TableName = SqlNodeDirectorySchema.ValidateTableName(tableName);
        }

        public Func<ValueTask<DbConnection>> ConnectionFactory { get; }

        public SqlNodeDirectoryDialect Dialect { get; }

        public string TableName { get; }
    }
}
