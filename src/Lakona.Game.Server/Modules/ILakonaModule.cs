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
    /// <param name="context">The final application configuration and service provider.</param>
    /// <param name="cancellationToken">Pass to blocking initialization work and stop further initialization when canceled.</param>
    Task StartAsync(
        ILakonaModuleContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stops module-owned work and releases module-owned runtime resources.
    /// </summary>
    /// <param name="cancellationToken">The shared cleanup deadline. End blocking waits when canceled, but still perform immediately available cleanup.</param>
    Task StopAsync(CancellationToken cancellationToken);
}
