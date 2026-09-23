using System.Reflection;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Scanning;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hotfix;

// Both DLL reload and in-process hosts compose the same generation. The caller
// owns the returned table/provider, including cleanup if validation fails.
internal static class HotfixRuntimeComposition
{
    internal static HotfixDispatchTable CreateDispatchTable(
        HotfixBehaviorScanResult scan, long tableVersion, IServiceProvider? rootServices)
    {
        var roleCatalog = rootServices?.GetService<NodeRoleCatalog>();
        var localActorMethods = roleCatalog is null
            ? scan.ActorMethods
            : scan.ActorMethods.Where(method => roleCatalog.IsLocal(method.ActorType)).ToArray();
        var localActorLifecycles = roleCatalog is null
            ? scan.ActorLifecycles
            : scan.ActorLifecycles.Where(lifecycle => roleCatalog.IsLocal(lifecycle.ActorType)).ToArray();
        var localBehaviorTypes = localActorMethods
            .Select(static method => method.BehaviorType)
            .Concat(localActorLifecycles.Select(static lifecycle => lifecycle.BehaviorType))
            .ToHashSet();
        var localMethods = roleCatalog is null
            ? scan.Methods
            : scan.Methods.Where(method => localBehaviorTypes.Contains(method.BehaviorType)).ToArray();
        var localHttpEndpoints = SelectLocalHttpEndpoints(scan.HttpEndpoints, rootServices);
        return new HotfixDispatchTable(
            tableVersion,
            localMethods,
            scan.Services,
            localActorMethods,
            localActorLifecycles,
            scan.TimerMethods,
            localHttpEndpoints,
            scan.Lifecycles);
    }

    private static IReadOnlyList<HotfixHttpEndpointMethodBinding> SelectLocalHttpEndpoints(
        IReadOnlyList<HotfixHttpEndpointMethodBinding> endpoints, IServiceProvider? rootServices)
    {
        var runtime = rootServices?.GetService<LakonaGameRuntimeOptions>();
        if (runtime is null)
        {
            return endpoints;
        }

        var enabledServices = runtime.Http.Listeners
            .SelectMany(static listener => listener.Services)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return endpoints
            .Where(endpoint => enabledServices.Contains(endpoint.Endpoint.Service))
            .ToArray();
    }

    internal static IReadOnlyList<HotfixActorHostDescriptor> CreateActorHostDescriptors(
        HotfixBehaviorScanResult scan,
        string? hotfixVersion)
    {
        var descriptors = new Dictionary<string, HotfixActorHostDescriptor>(StringComparer.OrdinalIgnoreCase);
        var actorTypes = scan.ActorMethods
            .Select(static method => method.ActorType)
            .Concat(scan.ActorLifecycles.Select(static lifecycle => lifecycle.ActorType))
            .Concat(scan.ActorStartups.Select(static startup => startup.ActorType))
            .Concat(scan.ActorPlacements.Select(static placement => placement.ActorType))
            .Distinct();
        foreach (var actorType in actorTypes)
        {
            AddActorHostDescriptor(
                descriptors,
                ActorNameConventions.Resolve(actorType),
                "placement:" + actorType.FullName,
                hotfixVersion);
        }

        foreach (var startup in scan.ActorStartups)
        {
            var actorType = startup.ActorType;
            var keyType = startup.KeyType;
            AddActorHostDescriptor(
                descriptors,
                ActorNameConventions.Resolve(actorType),
                $"startup:v1:{actorType.FullName}:{keyType.FullName}",
                hotfixVersion);
        }

        foreach (var placement in scan.ActorPlacements)
        {
            var name = ActorNameConventions.Resolve(placement.ActorType);
            AddActorHostDescriptor(
                descriptors,
                name,
                "placement:" + placement.ActorType.FullName,
                hotfixVersion);
        }

        return descriptors.Values
            .OrderBy(static descriptor => descriptor.Actor, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddActorHostDescriptor(
        IDictionary<string, HotfixActorHostDescriptor> descriptors,
        string actor,
        string policyHash,
        string? hotfixVersion)
    {
        descriptors[actor] = new HotfixActorHostDescriptor(
            actor,
            policyHash,
            string.IsNullOrWhiteSpace(hotfixVersion) ? "hotfix" : hotfixVersion);
    }

    internal static IServiceProvider BuildProvider(
        IReadOnlyList<ServiceDescriptor> startupServices,
        Assembly hotfixAssembly,
        IReadOnlyList<Type> moduleTypes,
        IServiceProvider? rootServices,
        List<string>? dependencyWarnings = null)
    {
        ArgumentNullException.ThrowIfNull(startupServices);
        ArgumentNullException.ThrowIfNull(hotfixAssembly);
        ArgumentNullException.ThrowIfNull(moduleTypes);

        var registrations = DiscoverGeneratedServiceRegistrations(hotfixAssembly);
        var rawServices = new ServiceCollection();
        foreach (var descriptor in startupServices)
        {
            ((ICollection<ServiceDescriptor>)rawServices).Add(descriptor);
        }

        foreach (var registration in registrations)
        {
            registration.Register(rawServices);
        }

        foreach (var moduleType in moduleTypes)
        {
            if (rawServices.All(descriptor => descriptor.ServiceType != moduleType))
            {
                ((ICollection<ServiceDescriptor>)rawServices).Add(
                    ServiceDescriptor.Singleton(moduleType, moduleType));
            }
        }

        var warnings = HotfixComponentDependencyPrecheck.Validate(rawServices.ToArray(), hotfixAssembly, rootServices);
        dependencyWarnings?.AddRange(warnings);
        var services = new ServiceCollection();
        var activationTracker = new HotfixActivationTracker();
        foreach (var descriptor in rawServices)
        {
            ((ICollection<ServiceDescriptor>)services).Add(
                CreateFallbackActivationDescriptor(descriptor, rootServices, activationTracker));
        }

        var hotfixProvider = services.BuildServiceProvider(validateScopes: true);
        return rootServices is null
            ? hotfixProvider
            : new FallbackServiceProvider(hotfixProvider, rootServices);
    }

    private static IReadOnlyList<IHotfixGeneratedServiceRegistration> DiscoverGeneratedServiceRegistrations(
        Assembly hotfixAssembly)
    {
        return hotfixAssembly
            .GetTypes()
            .Where(static type => !type.IsAbstract
                && !type.IsInterface
                && typeof(IHotfixGeneratedServiceRegistration).IsAssignableFrom(type))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .Select(static type =>
            {
                try
                {
                    return (IHotfixGeneratedServiceRegistration)Activator.CreateInstance(type)!;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Could not activate hotfix generated service registration '{type.FullName}'.",
                        ex);
                }
            })
            .ToArray();
    }

    private static ServiceDescriptor CreateFallbackActivationDescriptor(
        ServiceDescriptor descriptor,
        IServiceProvider? rootServices,
        HotfixActivationTracker activationTracker)
    {
        // Preserve native keyed activation; it does not use the two-provider fallback.
        if (descriptor.IsKeyedService)
        {
            if (descriptor.KeyedImplementationFactory is not { } keyedFactory) return descriptor;
            return ServiceDescriptor.DescribeKeyed(descriptor.ServiceType, descriptor.ServiceKey,
                (provider, key) =>
                {
                    using var activation = activationTracker.Enter(descriptor);
                    return keyedFactory(provider, key);
                }, descriptor.Lifetime);
        }

        if (descriptor.ImplementationInstance is not null)
        {
            return descriptor;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return ServiceDescriptor.Describe(
                descriptor.ServiceType,
                provider =>
                {
                    using var activation = activationTracker.Enter(descriptor);
                    return descriptor.ImplementationFactory(rootServices is null
                        ? provider
                        : new ActivationFallbackServiceProvider(provider, rootServices));
                },
                descriptor.Lifetime);
        }

        if (rootServices is not null && descriptor.ImplementationType is not null && !descriptor.ServiceType.IsGenericTypeDefinition)
        {
            return ServiceDescriptor.Describe(
                descriptor.ServiceType,
                provider =>
                {
                    using var activation = activationTracker.Enter(descriptor);
                    return ActivatorUtilities.CreateInstance(
                        new ActivationFallbackServiceProvider(provider, rootServices),
                        descriptor.ImplementationType);
                },
                descriptor.Lifetime);
        }

        return descriptor;
    }

    private sealed class ActivationFallbackServiceProvider(
        IServiceProvider hotfixServices,
        IServiceProvider rootServices) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IServiceProvider))
            {
                return this;
            }

            return TryGetCombinedEnumerable(serviceType, hotfixServices, rootServices, out var services)
                ? services
                : hotfixServices.GetService(serviceType) ?? rootServices.GetService(serviceType);
        }
    }

    private sealed class FallbackServiceProvider(
        IServiceProvider hotfixServices,
        IServiceProvider rootServices) : IServiceProvider, IDisposable, IAsyncDisposable
    {
        public object? GetService(Type serviceType)
        {
            return TryGetCombinedEnumerable(serviceType, hotfixServices, rootServices, out var services)
                ? services
                : hotfixServices.GetService(serviceType) ?? rootServices.GetService(serviceType);
        }

        public void Dispose()
        {
            (hotfixServices as IDisposable)?.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (hotfixServices is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                return;
            }

            (hotfixServices as IDisposable)?.Dispose();
        }
    }

    private static bool TryGetCombinedEnumerable(
        Type serviceType,
        IServiceProvider hotfixServices,
        IServiceProvider rootServices,
        out object? services)
    {
        services = null;
        if (!serviceType.IsGenericType ||
            serviceType.GetGenericTypeDefinition() != typeof(IEnumerable<>))
        {
            return false;
        }

        var elementType = serviceType.GetGenericArguments()[0];
        var hotfixItems = ToList(hotfixServices.GetService(serviceType));
        var rootItems = ToList(rootServices.GetService(serviceType));
        var combined = Array.CreateInstance(elementType, hotfixItems.Count + rootItems.Count);
        var index = 0;
        foreach (var item in hotfixItems)
        {
            combined.SetValue(item, index++);
        }

        foreach (var item in rootItems)
        {
            combined.SetValue(item, index++);
        }

        services = combined;
        return true;
    }

    private static List<object?> ToList(object? services)
    {
        var list = new List<object?>();
        if (services is System.Collections.IEnumerable enumerable)
        {
            foreach (var service in enumerable)
            {
                list.Add(service);
            }
        }

        return list;
    }
}
