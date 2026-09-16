using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Loading;

namespace Lakona.Game.Server.Hotfix;

internal static class HotfixResourceCleanup
{
    public static async ValueTask<IReadOnlyList<Exception>> RunAsync(
        HotfixDispatchTable? table, IServiceProvider? services, HotfixAssemblyLoadContext? context)
    {
        var failures = new List<Exception>();
        await DisposeAsync(table, "dispatch table", failures).ConfigureAwait(false);
        await DisposeAsync(services, "service provider", failures).ConfigureAwait(false);
        try { context?.Unload(); }
        catch (Exception exception) { failures.Add(Failure("load context", exception)); }
        return failures;
    }

    private static async ValueTask DisposeAsync(object? resource, string owner, List<Exception> failures)
    {
        try
        {
            if (resource is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (resource is IDisposable disposable)
                disposable.Dispose();
        }
        catch (Exception exception) { failures.Add(Failure(owner, exception)); }
    }

    internal static Exception Failure(string owner, Exception exception) =>
        new InvalidOperationException($"Hotfix cleanup failed for {owner}: {exception.Message}", exception);
}
