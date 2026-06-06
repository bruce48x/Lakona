using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Lakona.Game.Server.Guardrails.Rules;

namespace Lakona.Game.Server.Guardrails;

public static class LakonaGameGuardrailServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameRuntimeValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaGameValidationRule, NodeIdentityRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaGameValidationRule, EndpointRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaGameValidationRule, ClusterEndpointRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaGameValidationRule, HotfixSourceRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaGameValidationRule, ClusterServiceGraphRule>());
        services.TryAddSingleton<LakonaGameRuntimeValidator>();

        return services;
    }
}
