using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hosting;

public static class LakonaGameGeneratedServiceRegistrationDiscovery
{
    public static void RegisterDiscovered(IServiceCollection services)
    {
        RegisterDiscovered(services, DiscoverApplicationAssemblies());
    }

    internal static void RegisterDiscovered(
        IServiceCollection services,
        IReadOnlyList<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var registrationType in DiscoverRegistrationTypes(assemblies))
        {
            var registration = (ILakonaGameGeneratedServiceRegistration)Activator.CreateInstance(registrationType)!;
            registration.Register(services);
        }
    }

    internal static IReadOnlyList<Type> DiscoverRegistrationTypes(IReadOnlyList<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        return assemblies
            .SelectMany(GetLoadableTypes)
            .Where(static type => typeof(ILakonaGameGeneratedServiceRegistration).IsAssignableFrom(type)
                && !type.IsAbstract
                && !type.IsInterface
                && type.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    internal static IReadOnlyList<Assembly> DiscoverApplicationAssemblies()
    {
        var assemblies = new List<Assembly>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entryAssembly = Assembly.GetEntryAssembly();

        if (entryAssembly is not null)
        {
            AddAssembly(entryAssembly);
        }

        var entryName = entryAssembly?.GetName().Name ?? "";
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            var name = assembly.GetName().Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if ((!string.IsNullOrWhiteSpace(entryName)
                    && name.StartsWith(entryName, StringComparison.OrdinalIgnoreCase))
                || name.StartsWith("Server.App", StringComparison.OrdinalIgnoreCase))
            {
                AddAssembly(assembly);
            }
        }

        if (entryAssembly is not null)
        {
            AddAssembly(entryAssembly);
        }

        return assemblies;

        void AddAssembly(Assembly assembly)
        {
            if (assembly.IsDynamic)
            {
                return;
            }

            var name = assembly.GetName().Name;
            if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
            {
                assemblies.Add(assembly);
            }
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(static type => type is not null)!;
        }
    }
}
