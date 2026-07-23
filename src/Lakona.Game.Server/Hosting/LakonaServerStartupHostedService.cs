using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Health;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Hosting;

internal sealed class LakonaServerStartupHostedService(
    LakonaGameRuntimeOptions runtimeOptions,
    LakonaServerReadinessState readiness,
    ILogger<LakonaServerStartupHostedService> logger) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        readiness.MarkReady();
        logger.LogInformation(
            "Lakona server started successfully. NodeId={NodeId}.",
            runtimeOptions.Node.Id);
        return Task.CompletedTask;
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        readiness.MarkStopping();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
