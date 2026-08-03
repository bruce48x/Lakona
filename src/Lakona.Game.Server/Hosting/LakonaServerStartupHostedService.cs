using System.Reflection;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Health;
using Lakona.Game.Server.Hotfix.BuildTag;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Hosting;

internal sealed class LakonaServerStartupHostedService(
    LakonaGameRuntimeOptions runtimeOptions,
    LakonaServerReadinessState readiness,
    DistributedWorkAdmissionGate admissionGate,
    ILogger<LakonaServerStartupHostedService> logger) : IHostedLifecycleService
{
    private static readonly TimeSpan DistributedWorkDrainTimeout = TimeSpan.FromSeconds(30);

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
        var buildTag = HotfixBuildTag.Get(Assembly.GetEntryAssembly() ?? typeof(LakonaServerStartupHostedService).Assembly);
        logger.LogInformation(
            "Lakona server started successfully. NodeId={NodeId}. LakonaBuildTag={LakonaBuildTag}.",
            runtimeOptions.Node.Id,
            buildTag);
        return Task.CompletedTask;
    }

    public async Task StoppingAsync(CancellationToken cancellationToken)
    {
        readiness.MarkStopping();
        var drained = await admissionGate.CloseAndDrainAsync(
            DistributedWorkDrainTimeout,
            cancellationToken).ConfigureAwait(false);
        if (!drained)
        {
            throw new InvalidOperationException(
                "Distributed work did not drain before server listener shutdown.");
        }
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
