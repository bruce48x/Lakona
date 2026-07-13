using Lakona.Game.Cluster.Sql;
using Lakona.Game.Server.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lakona.Game.Server.Hosting;

internal sealed class LakonaClusterDirectorySchemaHostedService(
    LakonaGameRuntimeOptions runtimeOptions,
    IServiceProvider services) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        if (!runtimeOptions.Cluster.Directory.EnsureSchemaOnStartup)
        {
            return;
        }

        var options = services.GetService<SqlNodeDirectoryOptions>();
        if (options is null)
        {
            return;
        }

        await using var connection = await options.ConnectionFactory().ConfigureAwait(false);
        await SqlNodeDirectorySchema.EnsureCreatedAsync(
            connection,
            options.Dialect,
            options.TableName,
            cancellationToken).ConfigureAwait(false);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
