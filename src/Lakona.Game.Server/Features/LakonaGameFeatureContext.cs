using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Features;

public sealed class LakonaGameFeatureContext(
    IServiceCollection services,
    IConfiguration configuration,
    LakonaGameEndpointCatalog endpoints)
{
    public IServiceCollection Services { get; } = services;

    public IConfiguration Configuration { get; } = configuration;

    public LakonaGameEndpointCatalog Endpoints { get; } = endpoints;
}
