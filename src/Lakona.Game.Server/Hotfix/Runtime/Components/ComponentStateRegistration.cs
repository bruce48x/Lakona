using System.Reflection;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hotfix;

// Registration metadata only. The existing root DI owns all actual state instances.
internal sealed class ComponentStateRegistration(IReadOnlySet<Type> types)
{
    internal IReadOnlySet<Type> Types { get; } = types;

    internal static void Register(IServiceCollection services, IEnumerable<Assembly> assemblies)
    {
        var types = assemblies.Distinct().SelectMany(assembly => assembly.GetTypes())
            .OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();
        if (types.Where(type => !type.Assembly.IsCollectible
                && !type.Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Any(attribute =>
                    attribute.Key == "LakonaProjectRole" && attribute.Value == "Hotfix"))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Any(method => method.IsDefined(typeof(ComponentConfigureServicesAttribute), false)))
            throw new InvalidOperationException("ComponentConfigureServices belongs to a HotfixComponent in Hotfix, not stable App or State.");
        var states = types.Where(IsState).ToHashSet();
        foreach (var state in states)
        {
            Validate(state);
            if (services.Any(descriptor => descriptor.ServiceType == state))
                throw new InvalidOperationException($"Component state '{state.FullName}' must only be registered by the framework.");
            services.AddSingleton(state);
        }
        services.AddSingleton(new ComponentStateRegistration(states));
    }

    internal static bool IsState(Type type) => type.IsDefined(typeof(ComponentStateAttribute), false);

    internal static void Validate(Type type)
    {
        if (!type.IsClass || !type.IsPublic || !type.IsSealed || type.IsAbstract || type.IsGenericType
            || type.GetConstructor(Type.EmptyTypes) is null || type.Assembly.IsCollectible
            || !type.Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Any(attribute =>
                attribute.Key == "LakonaProjectRole" && attribute.Value == "ServerApp"))
            throw new InvalidOperationException($"Component state '{type.FullName}' must be a public sealed non-generic ServerApp class with a public parameterless constructor.");
    }
}
