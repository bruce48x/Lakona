using System.Reflection;
using System.Runtime.CompilerServices;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Loading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hotfix;

// After initialization, root DI retains stable Types and empty one-shot factories.
// The initial assembly transfers to the normal generation owner, never a second provider.
internal sealed class HotfixComponentRegistration : IDisposable
{
    private readonly List<(Type Service, SingletonFactory Factory)> factories = [];
    private readonly object gate = new();
    private HotfixAssemblyLoadContext? initialContext;
    private Assembly? initialAssembly;
    private HotfixAssemblySourceResult? initialSource;
    private int initialized;

    internal string AssemblyPath => initialSource!.AssemblyPath;

    internal static async ValueTask<HotfixComponentRegistration> PrepareAsync(
        IServiceCollection services, IConfiguration configuration, IReadOnlyList<string> roles,
        IHotfixAssemblySource source, IEnumerable<string> hostAssemblyNames)
    {
        var resolved = await source.ResolveAsync().ConfigureAwait(false);
        var context = new HotfixAssemblyLoadContext(resolved.AssemblyPath, hostAssemblyNames);
        try
        {
            var assembly = context.LoadMainAssemblyFromBytes(resolved.AssemblyPath);
            var registration = Configure(services, assembly, configuration, roles);
            registration.initialContext = context;
            registration.initialAssembly = assembly;
            registration.initialSource = resolved;
            return registration;
        }
        catch { context.Unload(); throw; }
    }

    internal static HotfixComponentRegistration Configure(IServiceCollection services, Assembly assembly,
        IConfiguration configuration, IReadOnlyList<string> roles)
    {
        var hooks = ValidateHooks(assembly);
        var original = services.ToHashSet();
        var registration = new HotfixComponentRegistration();
        try
        {
            foreach (var (type, method) in hooks)
                if (type.GetCustomAttribute<NodeRoleAttribute>() is not { } role
                    || roles.Contains(role.Role, StringComparer.Ordinal))
                    method.CreateDelegate<Action<IServiceCollection, IConfiguration>>()(services, configuration);
            if (original.Any(descriptor => ComponentStateRegistration.IsState(descriptor.ServiceType)
                && !services.Contains(descriptor)))
                throw new InvalidOperationException("ComponentConfigureServices cannot remove or replace framework-owned State registrations.");
            for (var index = 0; index < services.Count; index++)
            {
                var descriptor = services[index];
                if (original.Contains(descriptor)) continue;
                if (descriptor.IsKeyedService || descriptor.Lifetime != ServiceLifetime.Singleton
                    || !IsStableType(descriptor.ServiceType) || ComponentStateRegistration.IsState(descriptor.ServiceType)
                    || descriptor.ImplementationInstance is not null
                    || (descriptor.ImplementationType is { } implementation
                        && (!IsStableType(implementation) || ComponentStateRegistration.IsState(implementation))))
                    throw new InvalidOperationException($"ComponentConfigureServices registration '{descriptor.ServiceType}' must be an unkeyed singleton of closed stable types, using a type or factory, not an instance, component or State.");
                if (descriptor.ImplementationFactory is not { } factory) continue;
                var wrapper = new SingletonFactory(descriptor.ServiceType, factory);
                registration.factories.Add((descriptor.ServiceType, wrapper));
                services[index] = ServiceDescriptor.Singleton(descriptor.ServiceType, wrapper.Create);
            }
            return registration;
        }
        catch { registration.Dispose(); throw; }
    }

    // Every candidate checks declarations without rerunning application registration.
    internal static IReadOnlyList<(Type Type, MethodInfo Method)> ValidateHooks(Assembly assembly)
    {
        var hooks = new List<(Type, MethodInfo)>();
        foreach (var type in assembly.GetTypes().OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => method.IsDefined(typeof(ComponentConfigureServicesAttribute), false)).ToArray();
            if (methods.Length == 0) continue;
            if (!HotfixComponentCatalog.IsComponent(type) || methods.Length != 1 || !type.IsPublic || !type.IsSealed
                || type.IsAbstract || type.IsGenericType || type.GetConstructors().Length != 1
                || (!assembly.IsCollectible && !IsHotfixAssembly(assembly)))
                throw new InvalidOperationException($"'{type.FullName}' must be a HotfixComponent in Hotfix with at most one ComponentConfigureServices method.");
            var method = methods[0];
            var parameters = method.GetParameters();
            if (!method.IsPublic || !method.IsStatic || method.IsGenericMethod || method.ReturnType != typeof(void)
                || method.IsDefined(typeof(AsyncStateMachineAttribute), false) || parameters.Length != 2
                || parameters[0].ParameterType != typeof(IServiceCollection) || parameters[1].ParameterType != typeof(IConfiguration)
                || parameters.Any(parameter => parameter.IsOptional)
                || method.IsDefined(typeof(ComponentStartAttribute), false) || method.IsDefined(typeof(ComponentStopAttribute), false))
                throw new InvalidOperationException($"Component registration '{type.FullName}.{method.Name}' must be public static non-generic void(IServiceCollection, IConfiguration), without async or optional parameters.");
            hooks.Add((type, method));
        }
        return hooks;
    }

    internal void Initialize(IServiceProvider root)
    {
        if (Interlocked.Exchange(ref initialized, 1) != 0) return;
        try
        {
            // Include earlier descriptors hidden by last-registration semantics.
            // Their factories must also release the initial Hotfix generation.
            foreach (var service in factories.Select(item => item.Service).Distinct())
                _ = root.GetServices(service).ToArray();
        }
        finally { foreach (var (_, factory) in factories) factory.Clear(); }
    }

    internal (HotfixAssemblySourceResult Source, HotfixAssemblyLoadContext Context, Assembly Assembly)? TakeInitialAssembly()
    {
        lock (gate)
        {
            if (initialContext is null) return null;
            var result = (initialSource!, initialContext, initialAssembly!);
            initialSource = null;
            initialContext = null;
            initialAssembly = null;
            return result;
        }
    }

    public void Dispose()
    {
        foreach (var (_, factory) in factories) factory.Clear();
        var initial = TakeInitialAssembly();
        initial?.Context.Unload();
    }

    private static bool IsHotfixAssembly(Assembly assembly) => assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Any(attribute => attribute.Key == "LakonaProjectRole" && attribute.Value == "Hotfix");

    private static bool IsStableType(Type type) => !type.IsCollectible && !type.ContainsGenericParameters
        && !IsHotfixAssembly(type.Assembly) && !HotfixComponentCatalog.IsComponent(type)
        && (!type.HasElementType || IsStableType(type.GetElementType()!))
        && (!type.IsGenericType || type.GetGenericArguments().All(IsStableType));

    private sealed class SingletonFactory(Type service, Func<IServiceProvider, object> factory)
    {
        private Func<IServiceProvider, object>? pending = factory;

        internal object Create(IServiceProvider provider)
        {
            var activate = Interlocked.Exchange(ref pending, null)
                ?? throw new InvalidOperationException($"Component singleton '{service}' has a factory cycle or failed initialization. Restart the node.");
            var instance = activate(provider);
            if (instance is null || !IsStableType(instance.GetType()) || ComponentStateRegistration.IsState(instance.GetType())
                || instance is Delegate or Task or IServiceProvider)
            {
                // The rejected value has not transferred to DI ownership.
                if (instance is IAsyncDisposable asyncDisposable) asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                else (instance as IDisposable)?.Dispose();
                throw new InvalidOperationException($"Component singleton '{service}' must return a stable resource without Hotfix objects, delegates, tasks, providers or State.");
            }
            return instance;
        }

        internal void Clear() => Interlocked.Exchange(ref pending, null);
    }
}
