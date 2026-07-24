using Microsoft.Extensions.Hosting;

namespace Lakona.Game.Server.Hosting;

public static class LakonaGameServer
{
    /// <summary>
    /// Builds and runs a Lakona game server using an explicit infrastructure composition root.
    /// </summary>
    /// <param name="args">Command-line arguments forwarded to the .NET host.</param>
    /// <param name="configure">
    /// Configures endpoint implementations, cluster RPC, application services, and RPC bindings.
    /// </param>
    /// <returns>A task that completes with the process exit code after the host stops.</returns>
    public static async Task<int> RunAsync(
        string[] args,
        Action<LakonaGameServerBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var host = await LakonaGameServerBootstrapper
            .BuildAsync(args, configure)
            .ConfigureAwait(false);
        await LakonaGameServerRunner.RunAsync(host).ConfigureAwait(false);
        return 0;
    }

    public static async Task LoadInitialHotfixAsync(IHost host)
    {
        await LakonaGameServerRunner.LoadInitialHotfixAsync(host).ConfigureAwait(false);
    }
}
