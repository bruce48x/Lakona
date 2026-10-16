using System.Reflection;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hotfix;

// Generation-owned metadata. Never retain this catalog in the stable lifecycle record.
internal sealed class HotfixComponentCatalog(IReadOnlyList<HotfixComponentDescriptor> ordered)
{
    internal IReadOnlyList<HotfixComponentDescriptor> Ordered { get; } = ordered;

    internal static HotfixComponentCatalog Compose(ServiceCollection services, Assembly assembly,
        IReadOnlyList<ServiceDescriptor> customServices, IServiceProvider? root)
    {
        var all = assembly.GetTypes().Where(IsComponent).OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();
        HotfixComponentRegistration.ValidateHooks(assembly);
        var exceptions = all.Where(type => typeof(Exception).IsAssignableFrom(type)).ToArray();
        if (exceptions.Length != 0)
            throw new InvalidOperationException($"HotfixComponent cannot mark exception types ({string.Join(", ", exceptions.Select(type => type.FullName))}); remove the attribute.");
        var roles = root?.GetService<LakonaGameRuntimeOptions>()?.Node.Roles;
        var roleCatalog = root?.GetService<NodeRoleCatalog>();
        var local = all.Where(type => type.GetCustomAttribute<NodeRoleAttribute>() is not { } role
            || (roleCatalog is not null ? roleCatalog.IsLocal(type) : roles is null || roles.Contains(role.Role, StringComparer.Ordinal))).ToHashSet();
        foreach (var registration in customServices)
            if (RefersToComponent(registration.ServiceType)
                || (!registration.IsKeyedService && registration.ImplementationType is { } implementation && all.Contains(implementation)))
                throw new InvalidOperationException($"Hotfix component registration '{registration.ServiceType}' cannot be overridden or aliased. Inject its concrete type.");

        // Filter before DI construction. Do not resolve a role-inapplicable component even for validation.
        foreach (var registration in services.ToArray())
        {
            if (!all.Contains(registration.ServiceType) && RefersToComponent(registration.ServiceType))
                throw new InvalidOperationException($"Hotfix component registration '{registration.ServiceType}' cannot be overridden or aliased. Inject its concrete type.");
            if (ComponentStateRegistration.IsState(registration.ServiceType))
                throw new InvalidOperationException($"Stable component state '{registration.ServiceType}' cannot be registered in Hotfix.");
            if (all.Contains(registration.ServiceType) && !local.Contains(registration.ServiceType))
                services.Remove(registration);
        }

        var descriptors = new Dictionary<Type, HotfixComponentDescriptor>();
        var stateOwners = new Dictionary<Type, Type>();
        foreach (var type in all)
        {
            if (!type.IsPublic || !type.IsSealed || type.IsAbstract || type.IsGenericType
                || typeof(Exception).IsAssignableFrom(type) || type.GetConstructors().Length != 1)
                throw new InvalidOperationException($"Hotfix component '{type.FullName}' must be public sealed non-generic with exactly one public constructor.");
            var parameters = type.GetConstructors()[0].GetParameters();
            if (parameters.Any(parameter => ComponentStateRegistration.IsUnsupportedAppDependency(parameter.ParameterType)))
                throw new InvalidOperationException($"Component '{type}' requires directly injected concrete [ComponentState] for custom ServerApp dependencies; object and unmarked App interfaces/wrappers are unsupported. Move business logic to HotfixComponent.");
            if (parameters.Any(parameter => ComponentStateRegistration.RefersToState(parameter.ParameterType)
                && !ComponentStateRegistration.IsState(parameter.ParameterType)))
                throw new InvalidOperationException($"Component '{type}' must inject ComponentState directly by concrete type; state collections and wrappers are unsupported.");
            var states = parameters.Where(parameter => ComponentStateRegistration.IsState(parameter.ParameterType)).ToArray();
            if (states.Length > 1)
                throw new InvalidOperationException($"Component '{type.FullName}' may own at most one state.");
            var state = states.SingleOrDefault()?.ParameterType;
            // Includes auto-property backing fields and primary-constructor captures.
            var stateFields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(field => ComponentStateRegistration.RefersToState(field.FieldType)).ToArray();
            if (stateFields.Length > 1)
                throw new InvalidOperationException($"Component '{type}' may retain ComponentState in at most one field or auto-property.");
            if (stateFields.Any(field => field.FieldType != state))
                throw new InvalidOperationException($"Component '{type}' must store the directly injected concrete ComponentState, without state collections or wrappers.");
            if (state is not null)
            {
                if (states[0].IsOptional || states[0].IsDefined(typeof(FromKeyedServicesAttribute)))
                    throw new InvalidOperationException($"Component state '{state}' must be a required, unkeyed parameter.");
                ComponentStateRegistration.Validate(state);
                if (!stateOwners.TryAdd(state, type))
                    throw new InvalidOperationException($"Component state '{state.FullName}' has multiple owners: '{stateOwners[state]}' and '{type}'.");
                if (local.Contains(type) && root?.GetService<ComponentStateRegistration>()?.Types.Contains(state) != true)
                    throw new InvalidOperationException($"Component state '{state.FullName}' was not registered in the stable application root.");
            }
            var start = Hook<ComponentStartAttribute>(type);
            var stop = Hook<ComponentStopAttribute>(type);
            if (start is not null && start == stop)
                throw new InvalidOperationException($"Component '{type}' cannot use both lifecycle attributes on one method.");
            if (!local.Contains(type)) continue;
            var registration = services.Where(item => item.ServiceType == type).ToArray();
            if (registration.Length != 1 || registration[0].IsKeyedService
                || registration[0].ImplementationType != type || registration[0].Lifetime != ServiceLifetime.Singleton)
                throw new InvalidOperationException($"Component '{type}' requires exactly one concrete singleton registration.");
            var dependencies = new List<Type>();
            foreach (var parameter in parameters)
            {
                var dependency = parameter.ParameterType;
                if (dependency == typeof(IServiceProvider) || dependency == typeof(IServiceScopeFactory))
                    throw new InvalidOperationException($"Component '{type}' must declare dependencies directly, without a service locator.");
                if (all.Contains(dependency))
                {
                    if (!local.Contains(dependency))
                        throw new InvalidOperationException($"Component '{type}' depends on '{dependency}' which is disabled on this node.");
                    if (parameter.IsOptional || parameter.IsDefined(typeof(FromKeyedServicesAttribute)))
                        throw new InvalidOperationException($"Component dependency '{type}' -> '{dependency}' must be a required, unkeyed concrete parameter.");
                    dependencies.Add(dependency);
                }
                else if (RefersToComponent(dependency))
                    throw new InvalidOperationException($"Component '{type}' must inject components by concrete type, not '{dependency}'.");
            }
            descriptors.Add(type, new(type, state, dependencies, start, stop));
        }

        // Also enforce ownership and concrete injection for Behavior/HTTP/service consumers.
        foreach (var type in services.Where(item => !item.IsKeyedService).Select(item => item.ImplementationType)
                     .OfType<Type>().Where(type => type.Assembly == assembly))
        foreach (var parameter in type.GetConstructors().SelectMany(constructor => constructor.GetParameters()))
        {
            var dependency = parameter.ParameterType;
            if (ComponentStateRegistration.RefersToState(dependency) && !ComponentStateRegistration.IsState(dependency))
                throw new InvalidOperationException($"'{type}' must not inject ComponentState through collections or wrappers; inject its owner.");
            if (all.Contains(dependency) && (parameter.IsOptional || parameter.IsDefined(typeof(FromKeyedServicesAttribute))))
                throw new InvalidOperationException($"'{type}' must inject component '{dependency}' as a required, unkeyed concrete parameter.");
            if (ComponentStateRegistration.IsState(dependency)
                && (!stateOwners.TryGetValue(dependency, out var owner) || owner != type))
                throw new InvalidOperationException($"'{type}' cannot inject component state '{dependency}'; inject its owner.");
            if ((all.Contains(dependency) && !local.Contains(dependency))
                || (!all.Contains(dependency) && RefersToComponent(dependency)))
                throw new InvalidOperationException($"'{type}' must inject an enabled concrete component, not '{dependency}'.");
        }
        foreach (var method in assembly.GetTypes().Where(type => !IsComponent(type)).SelectMany(type =>
                     type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)))
            if (method.IsDefined(typeof(ComponentStartAttribute)) || method.IsDefined(typeof(ComponentStopAttribute)))
                throw new InvalidOperationException($"Component lifecycle method '{method}' must belong to a HotfixComponent.");

        var order = new List<HotfixComponentDescriptor>();
        var visiting = new HashSet<Type>();
        var visited = new HashSet<Type>();
        foreach (var type in local.OrderBy(type => type.FullName, StringComparer.Ordinal)) Visit(type);
        return new(order);

        bool RefersToComponent(Type dependency) =>
            all.Any(component => dependency.IsAssignableFrom(component))
            || (dependency.IsArray && RefersToComponent(dependency.GetElementType()!))
            || (dependency.IsGenericType
                && dependency.GetGenericTypeDefinition() != typeof(Microsoft.Extensions.Logging.ILogger<>)
                && dependency.GetGenericArguments().Any(RefersToComponent));

        void Visit(Type type)
        {
            if (visited.Contains(type)) return;
            if (!visiting.Add(type)) throw new InvalidOperationException($"Component dependency cycle at '{type}'.");
            foreach (var dependency in descriptors[type].Dependencies.OrderBy(type => type.FullName, StringComparer.Ordinal)) Visit(dependency);
            visiting.Remove(type);
            visited.Add(type);
            order.Add(descriptors[type]);
        }
    }

    internal static bool IsComponent(Type type) => type.IsDefined(typeof(HotfixComponentAttribute), false);

    private static MethodInfo? Hook<T>(Type type) where T : Attribute
    {
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.IsDefined(typeof(T), false)).ToArray();
        if (methods.Length > 1) throw new InvalidOperationException($"Component '{type}' has duplicate {typeof(T).Name} hooks.");
        var method = methods.SingleOrDefault();
        if (method is null) return null;
        var parameters = method.GetParameters();
        if (!method.IsPublic || method.IsStatic || method.IsGenericMethod || method.ReturnType != typeof(ValueTask)
            || parameters.Length != 1 || parameters[0].ParameterType != typeof(CancellationToken) || parameters[0].IsOptional)
            throw new InvalidOperationException($"Component hook '{type}.{method.Name}' must be public instance non-generic ValueTask(CancellationToken).");
        return method;
    }
}

internal sealed record HotfixComponentDescriptor(Type Type, Type? State, IReadOnlyList<Type> Dependencies,
    MethodInfo? Start, MethodInfo? Stop)
{
    internal string Identity => Type.FullName!;

    internal ValueTask InvokeAsync(IServiceProvider services, bool start, CancellationToken cancellationToken)
    {
        var instance = services.GetRequiredService(Type);
        var method = start ? Start : Stop;
        return method is null ? default : method.CreateDelegate<Func<CancellationToken, ValueTask>>(instance)(cancellationToken);
    }
}
