using Lakona.Game.Server.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Modules;

internal sealed class LakonaModuleRuntime(
    LakonaModuleCatalog catalog,
    IConfiguration configuration,
    IServiceProvider services,
    LakonaServerReadinessState readiness,
    ILogger<LakonaModuleRuntime> logger)
{
    private readonly object gate = new();
    private readonly List<LakonaModuleRegistration> started = [];
    private bool startAttempted;
    private bool stopped;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (startAttempted)
            {
                throw new InvalidOperationException(
                    "Lakona application modules can only be started once.");
            }

            startAttempted = true;
        }

        var context = new LakonaModuleContext(configuration, services);
        foreach (var registration in catalog.Modules)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await registration.Instance
                    .StartAsync(context, cancellationToken)
                    .ConfigureAwait(false);
                lock (gate)
                {
                    started.Add(registration);
                }

                logger.LogInformation(
                    "Lakona application module {ModuleType} started.",
                    registration.ModuleType.FullName);
            }
            catch (Exception exception)
            {
                readiness.MarkFailed(registration.ModuleType, exception);
                var rollbackFailures = await StopStartedAsync(
                    CancellationToken.None,
                    "rollback").ConfigureAwait(false);
                foreach (var rollbackFailure in rollbackFailures)
                {
                    logger.LogError(
                        rollbackFailure.Exception,
                        "Lakona application module {ModuleType} failed during startup rollback.",
                        rollbackFailure.ModuleType.FullName);
                }

                throw;
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (stopped)
            {
                return;
            }

            stopped = true;
        }

        var failures = await StopStartedAsync(
            cancellationToken,
            "shutdown").ConfigureAwait(false);
        if (failures.Count == 0)
        {
            return;
        }

        throw new AggregateException(
            "One or more Lakona application modules failed to stop.",
            failures.Select(static failure => new InvalidOperationException(
                $"Lakona application module '{failure.ModuleType.FullName}' failed to stop.",
                failure.Exception)));
    }

    private async Task<IReadOnlyList<ModuleStopFailure>> StopStartedAsync(
        CancellationToken cancellationToken,
        string operation)
    {
        LakonaModuleRegistration[] snapshot;
        lock (gate)
        {
            snapshot = started.AsEnumerable().Reverse().ToArray();
            started.Clear();
        }

        var failures = new List<ModuleStopFailure>();
        foreach (var registration in snapshot)
        {
            try
            {
                await registration.Instance
                    .StopAsync(cancellationToken)
                    .ConfigureAwait(false);
                logger.LogInformation(
                    "Lakona application module {ModuleType} stopped during {Operation}.",
                    registration.ModuleType.FullName,
                    operation);
            }
            catch (Exception exception)
            {
                failures.Add(new ModuleStopFailure(
                    registration.ModuleType,
                    exception));
            }
        }

        return failures;
    }

    private sealed record LakonaModuleContext(
        IConfiguration Configuration,
        IServiceProvider Services) : ILakonaModuleContext;

    private sealed record ModuleStopFailure(
        Type ModuleType,
        Exception Exception);
}
