using Microsoft.Extensions.Hosting;
using System.Reflection;

namespace Lakona.Game.Server.Hosting;

public static class LakonaGameServer
{
    /// <summary>Builds the production server without starting it. The caller owns startup, shutdown and disposal.</summary>
    public static Task<IHost> BuildAsync(string[] args, Action<LakonaGameServerBuilder> configure) =>
        LakonaGameServerBootstrapper.BuildAsync(args, configure);

    /// <summary>Builds the production server using explicit App assemblies and a Hotfix artifact base directory.</summary>
    /// <remarks>Uses normal configuration, Hotfix loading, readiness and listeners. Call StartAsync before
    /// using Actors, then StopAsync and dispose the host. The base directory defaults to AppContext.BaseDirectory;
    /// it does not change the process working directory.</remarks>
    public static Task<IHost> BuildAsync(string[] args, Action<LakonaGameServerBuilder> configure,
        IReadOnlyList<Assembly> applicationAssemblies, string? baseDirectory = null) =>
        LakonaGameServerBootstrapper.BuildAsync(args, configure, applicationAssemblies, baseDirectory);

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
}
