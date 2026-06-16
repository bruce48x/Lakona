using Lakona.Game.Cluster.Sql;
using Microsoft.Extensions.Hosting;

namespace Server.App.Features;

internal sealed class AgarDatabaseSchemaHostedService : IHostedService
{
    private readonly AgarDatabaseConnectionFactory _connections;
    private readonly SqlNodeDirectoryOptions _nodeDirectoryOptions;

    public AgarDatabaseSchemaHostedService(
        AgarDatabaseConnectionFactory connections,
        SqlNodeDirectoryOptions nodeDirectoryOptions)
    {
        _connections = connections;
        _nodeDirectoryOptions = nodeDirectoryOptions;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var connection = _connections.CreatePostgresConnection();
        await SqlNodeDirectorySchema.EnsureCreatedAsync(
                connection,
                _nodeDirectoryOptions.Dialect,
                _nodeDirectoryOptions.TableName,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
