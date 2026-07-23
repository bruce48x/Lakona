using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Modules;

/// <summary>
/// Owns the registration and operational lifecycle of one stable application resource group.
/// </summary>
/// <remarks>
/// Lakona discovers modules from stable application assemblies. Registration is
/// synchronous and happens before the root provider is built. Runtime resources
/// must be initialized in <see cref="StartAsync"/> and released in
/// <see cref="StopAsync"/>.
/// </remarks>
public interface ILakonaModule
{
    /// <summary>
    /// Declares the module's stable object graph before the root provider is built.
    /// </summary>
    void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration);

    /// <summary>
    /// Completes all work required for the module to serve application traffic.
    /// </summary>
    Task StartAsync(
        ILakonaModuleContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stops module-owned work and releases module-owned runtime resources.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken);
}
