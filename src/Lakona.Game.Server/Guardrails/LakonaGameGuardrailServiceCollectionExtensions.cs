using Lakona.Game.Server.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lakona.Game.Server.Guardrails;

public static class LakonaGameGuardrailServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameRuntimeValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(provider => new LakonaGameRuntimeValidator(
            provider.GetService<LakonaEndpointRuntimeRegistry>()));

        return services;
    }
}
