using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Features;

/// <summary>
/// Provides services, configuration, and endpoint metadata to stable feature lifecycle hooks.
/// </summary>
/// <param name="services">The service collection used to build the host.</param>
/// <param name="configuration">The host configuration.</param>
/// <param name="endpoints">The resolved game endpoint catalog.</param>
public sealed class LakonaGameFeatureContext(
    IServiceCollection services,
    IConfiguration configuration,
    LakonaGameEndpointCatalog endpoints)
{
    /// <summary>
    /// Gets the service collection used to build the host.
    /// </summary>
    public IServiceCollection Services { get; } = services;

    /// <summary>
    /// Gets the host configuration.
    /// </summary>
    public IConfiguration Configuration { get; } = configuration;

    /// <summary>
    /// Gets the resolved game endpoint catalog.
    /// </summary>
    public LakonaGameEndpointCatalog Endpoints { get; } = endpoints;
}
