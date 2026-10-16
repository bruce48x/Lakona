using System.Reflection;
using System.Runtime.CompilerServices;
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

    internal static bool RefersToState(Type type) => IsState(type)
        || (type.HasElementType && RefersToState(type.GetElementType()!))
        || (type.IsGenericType && type.GetGenericTypeDefinition() != typeof(Microsoft.Extensions.Logging.ILogger<>)
            && type.GetGenericArguments().Any(RefersToState));

    internal static bool IsUnsupportedAppDependency(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Microsoft.Extensions.Logging.ILogger<>)
            || type.FullName == "Lakona.Game.Server.Hotfix.ActorAccess") return false;
        if (type == typeof(object)) return true;
        if (RefersToState(type)) return false; // Preserve the existing direct-state/wrapper validation.
        return IsServerApp(type)
            || (type.HasElementType && IsUnsupportedAppDependency(type.GetElementType()!))
            || (type.IsGenericType && type.GetGenericArguments().Any(IsUnsupportedAppDependency));
    }

    private static bool IsServerApp(Type type) => type.Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Any(attribute =>
        attribute.Key == "LakonaProjectRole" && string.Equals(attribute.Value?.Trim(), "ServerApp", StringComparison.OrdinalIgnoreCase));

    internal static void Validate(Type type)
    {
        if (!type.IsClass || !type.IsPublic || !type.IsSealed || type.IsAbstract || type.IsGenericType
            || type.GetConstructor(Type.EmptyTypes) is null || type.Assembly.IsCollectible
            || !IsServerApp(type))
            throw new InvalidOperationException($"Component state '{type.FullName}' must be a public sealed non-generic ServerApp class with a public parameterless constructor.");
        var disposal = new[] { typeof(IDisposable), typeof(IAsyncDisposable) }.Where(contract => contract.IsAssignableFrom(type))
            .SelectMany(contract => type.GetInterfaceMap(contract).TargetMethods).ToHashSet();
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            const BindingFlags members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            if (current.GetMethods(members).Any(method => (!method.IsSpecialName || method.Name.StartsWith("op_", StringComparison.Ordinal))
                && !method.IsDefined(typeof(CompilerGeneratedAttribute), false) && !disposal.Contains(method)))
                throw new InvalidOperationException($"Component state '{type.FullName}' cannot contain ordinary methods or operators; move logic to its HotfixComponent and retain only data and IDisposable/IAsyncDisposable implementations.");
            if (current.GetProperties(members).Any(property => property.GetAccessors(true)
                .Any(accessor => !accessor.IsDefined(typeof(CompilerGeneratedAttribute), false))))
                throw new InvalidOperationException($"Component state '{type.FullName}' must use auto-properties, without computed properties or indexers.");
        }
    }
}
