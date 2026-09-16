using System.Reflection;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hotfix;

// Check registration metadata only. Never construct a component or execute a factory.
internal static class HotfixComponentDependencyPrecheck
{
    public static IReadOnlyList<string> Validate(IReadOnlyList<ServiceDescriptor> services, Assembly assembly, IServiceProvider? root)
    {
        var rootMetadata = root?.GetService(typeof(IServiceProviderIsService)) as IServiceProviderIsService;
        var errors = new SortedSet<string>(StringComparer.Ordinal);
        var warnings = new SortedSet<string>(StringComparer.Ordinal);
        var pending = new Stack<Visit>();
        var visited = new HashSet<(ServiceDescriptor Descriptor, Type Type, bool Singleton)>();
        foreach (var component in assembly.GetTypes().Where(type => type.IsDefined(typeof(HotfixComponentAttribute), false))
                     .OrderByDescending(type => type.FullName, StringComparer.Ordinal))
        {
            Resolve(component, null, optional: false);
        }

        while (pending.TryPop(out var visit))
        {
            var descriptor = visit.Descriptor;
            var singleton = visit.Parent?.Singleton == true || descriptor.Lifetime == ServiceLifetime.Singleton;
            var path = new Path(visit.Type, descriptor, singleton, visit.Parent);
            if (visit.Parent?.Singleton == true && descriptor.Lifetime == ServiceLifetime.Scoped)
            {
                errors.Add($"{path}: singleton component dependency reaches scoped service. Use a generation singleton dependency or resolve the scoped service inside an explicit operation scope.");
                continue;
            }
            if (Ancestors(visit.Parent).Any(parent => ReferenceEquals(parent.Descriptor, descriptor) && parent.Type == visit.Type))
            {
                errors.Add($"{path}: dependency cycle. Remove the circular constructor dependency or extract a shared dependency.");
                continue;
            }
            if (!visited.Add((descriptor, visit.Type, singleton))) continue;
            if (Ancestors(visit.Parent).Any(parent => ReferenceEquals(parent.Descriptor, descriptor)))
            {
                warnings.Add($"Hotfix dependency coverage incomplete: {path} recursively closes the same generic registration with different types. Test this resolution explicitly.");
                continue;
            }
            if (descriptor.ImplementationInstance is not null) continue;
            if (descriptor.ImplementationFactory is not null)
            {
                warnings.Add($"Hotfix dependency coverage incomplete: {path} uses a factory; its dependencies cannot be verified from registration metadata. Test this resolution before serving requests.");
                continue;
            }
            var implementation = descriptor.ImplementationType;
            if (implementation is null) continue;
            if (implementation.IsGenericTypeDefinition)
            {
                try { implementation = implementation.MakeGenericType(visit.Type.GetGenericArguments()); }
                catch (ArgumentException exception)
                {
                    errors.Add($"{path}: generic registration cannot be closed: {exception.Message}");
                    continue;
                }
            }
            if (implementation.IsAbstract || !visit.Type.IsAssignableFrom(implementation))
            {
                errors.Add($"{path}: implementation '{implementation.FullName}' is not a concrete assignable service. Correct the registration.");
                continue;
            }
            var constructors = implementation.GetConstructors();
            // Multiple-constructor selection differs between native DI and fallback activation.
            // Do not invent a selection rule or reject a viable alternative constructor.
            if (constructors.Length != 1)
            {
                if (constructors.Length == 0)
                    errors.Add($"{path}: '{implementation.FullName}' has no public constructor. Provide a public constructor or an explicit factory.");
                else
                    warnings.Add($"Hotfix dependency coverage incomplete: {path} has multiple public constructors; constructor selection requires activation. Prefer one public constructor and test resolution.");
                continue;
            }
            foreach (var parameter in constructors[0].GetParameters().Reverse())
            {
                if (parameter.IsDefined(typeof(FromKeyedServicesAttribute), false) || parameter.IsDefined(typeof(ServiceKeyAttribute), false))
                {
                    warnings.Add($"Hotfix dependency coverage incomplete: {path}, parameter '{parameter.Name}' uses keyed resolution; verify its key and registration through an activation test.");
                    continue;
                }
                Resolve(parameter.ParameterType, path, parameter.HasDefaultValue);
            }
        }

        if (errors.Count != 0)
            throw new InvalidOperationException("Hotfix component dependency precheck failed:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Concat(warnings)));
        return warnings.ToArray();

        void Resolve(Type type, Path? parent, bool optional)
        {
            if (type == typeof(IServiceProvider) || type == typeof(IServiceScopeFactory) ||
                type == typeof(IServiceProviderIsService) || type == typeof(IServiceProviderIsKeyedService)) return;
            var registration = services.LastOrDefault(item => !item.IsKeyedService && item.ServiceType == type)
                ?? services.LastOrDefault(item => Matches(item, type));
            if (registration is not null)
            {
                pending.Push(new Visit(registration, type, parent));
                return;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                var element = type.GetGenericArguments()[0];
                // Fallback combines all generation registrations with root registrations.
                foreach (var descriptor in services.Where(item => Matches(item, element) && CanClose(item, element)).Reverse())
                    pending.Push(new Visit(descriptor, element, parent));
                return; // An empty collection is a valid dependency.
            }
            if (rootMetadata?.IsService(type) == true) return;
            if (root is not null && rootMetadata is null)
            {
                warnings.Add($"Hotfix dependency coverage incomplete: {Chain(parent, type)}; the stable provider has no IServiceProviderIsService metadata. Verify registration without relying on this precheck.");
                return;
            }
            if (!optional)
                errors.Add($"{Chain(parent, type)}: service is not registered. Register it in HotfixConfigureServices or the stable application container, or remove the dependency.");
        }
    }

    private static bool Matches(ServiceDescriptor descriptor, Type type) => !descriptor.IsKeyedService &&
        (descriptor.ServiceType == type || type.IsConstructedGenericType && descriptor.ServiceType.IsGenericTypeDefinition &&
            descriptor.ServiceType == type.GetGenericTypeDefinition());

    private static bool CanClose(ServiceDescriptor descriptor, Type type)
    {
        if (descriptor.ImplementationType is not { IsGenericTypeDefinition: true } implementation) return true;
        try { implementation.MakeGenericType(type.GetGenericArguments()); return true; }
        catch (ArgumentException) { return false; } // Native DI excludes incompatible open registrations from IEnumerable<T>.
    }

    private static IEnumerable<Path> Ancestors(Path? path)
    {
        for (; path is not null; path = path.Parent) yield return path;
    }

    private static string Chain(Path? parent, Type type) => string.Join(" -> ",
        Ancestors(parent).Reverse().Select(item => item.Type.FullName ?? item.Type.Name).Append(type.FullName ?? type.Name));

    private sealed record Path(Type Type, ServiceDescriptor Descriptor, bool Singleton, Path? Parent)
    {
        public override string ToString() => Chain(Parent, Type);
    }

    private sealed record Visit(ServiceDescriptor Descriptor, Type Type, Path? Parent);
}
