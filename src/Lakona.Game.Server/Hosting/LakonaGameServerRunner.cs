using System.Runtime.ExceptionServices;
using Lakona.Game.Server.Health;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Hosting;

internal static class LakonaGameServerRunner
{
    internal static Task RunAsync(IHost host)
    {
        return RunAsync(host, LoadInitialHotfixAsync);
    }

    internal static async Task RunAsync(
        IHost host,
        Func<IHost, Task> loadInitialHotfixAsync)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(loadInitialHotfixAsync);

        var modules = host.Services.GetRequiredService<LakonaModuleRuntime>();
        var readiness = host.Services.GetRequiredService<LakonaServerReadinessState>();
        var logger = host.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Server.ApplicationModules");
        Exception? failure = null;
        var frameworkStartAttempted = false;

        try
        {
            await modules.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await loadInitialHotfixAsync(host).ConfigureAwait(false);
            frameworkStartAttempted = true;
            await host.RunAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
            readiness.MarkStopping();
            if (frameworkStartAttempted)
            {
                try
                {
                    await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception stopException)
                {
                    TryLogCleanupFailure(
                        logger,
                        stopException,
                        "Lakona framework cleanup failed after startup or runtime failure.");
                }
            }
        }

        readiness.MarkStopping();
        try
        {
            await modules.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (failure is null)
            {
                failure = exception;
            }
            else
            {
                TryLogCleanupFailure(
                    logger,
                    exception,
                    "Lakona application module cleanup failed while preserving an earlier server failure.");
            }
        }

        try
        {
            if (host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                host.Dispose();
            }
        }
        catch (Exception exception)
        {
            if (failure is null)
            {
                failure = exception;
            }
            else
            {
                TryLogCleanupFailure(
                    logger,
                    exception,
                    "Lakona root provider disposal failed while preserving an earlier server failure.");
            }
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    internal static async Task LoadInitialHotfixAsync(IHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        using var scope = host.Services.CreateScope();
        var hotfix = scope.ServiceProvider.GetRequiredService<IHotfixManager>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Server.Hotfix");
        var result = await hotfix.ReloadAsync().ConfigureAwait(false);

        if (result.Succeeded)
        {
            logger.LogInformation(
                "Initial hotfix load succeeded from {HotfixPath} with {MethodCount} method(s).",
                result.Current.SourcePath,
                result.Current.Methods.Count);
            return;
        }

        var diagnostics = result.Diagnostics.Count == 0
            ? ""
            : " Diagnostics: " + string.Join("; ", result.Diagnostics);
        var message =
            $"Initial hotfix load failed for '{result.RequestedPath}': {result.ErrorMessage}{diagnostics}";
        logger.LogError("{Message}", message);
        throw new InvalidOperationException(message);
    }

    private static void TryLogCleanupFailure(
        ILogger logger,
        Exception exception,
        string message)
    {
        try
        {
            logger.LogError(exception, message);
        }
        catch
        {
            // Cleanup diagnostics must never replace the primary server failure.
        }
    }
}
