using System.Reflection;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.BuildTag;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Hosting;

internal static class LakonaGameServerRunner
{
    internal static Task RunAsync(IHost host)
    {
        return RunHostAsync(host);
    }

    private static async Task RunHostAsync(IHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        try
        {
            await host.RunAsync().ConfigureAwait(false);
        }
        finally
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
    }

    internal static async Task LoadInitialHotfixAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        using var scope = services.CreateScope();
        var hotfix = scope.ServiceProvider.GetRequiredService<IHotfixManager>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Server.Hotfix");
        var result = await hotfix.ReloadAsync(cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
        {
            logger.LogInformation(
                "Initial hotfix load succeeded from {HotfixPath} with {MethodCount} method(s). LakonaBuildTag={LakonaBuildTag}. Version={Version}.",
                result.Current.SourcePath,
                result.Current.Methods.Count,
                HotfixBuildTag.Get(Assembly.GetEntryAssembly() ?? typeof(LakonaGameServerRunner).Assembly),
                result.Current.Version);
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

}
