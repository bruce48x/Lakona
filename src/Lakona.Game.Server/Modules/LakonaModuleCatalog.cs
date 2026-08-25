using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Modules;

internal sealed record LakonaModuleRegistration(
    Type ModuleType,
    ILakonaModule Instance);

internal sealed class LakonaModuleCatalog(
    IReadOnlyList<LakonaModuleRegistration> modules)
{
    public IReadOnlyList<LakonaModuleRegistration> Modules { get; } =
        modules ?? throw new ArgumentNullException(nameof(modules));
}

internal static class LakonaModuleDiscovery
{
    internal static LakonaModuleCatalog Configure(
        IServiceCollection services,
        IConfiguration configuration,
        IEnumerable<Assembly> assemblies,
        bool excludeTestAssemblies = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(assemblies);

        var moduleTypes = DiscoverTypes(assemblies, excludeTestAssemblies);
        return ConfigureTypes(services, configuration, moduleTypes);
    }

    internal static LakonaModuleCatalog ConfigureTypes(
        IServiceCollection services,
        IConfiguration configuration,
        IReadOnlyList<Type> moduleTypes)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(moduleTypes);

        foreach (var moduleType in moduleTypes)
        {
            ValidateModuleType(moduleType);
        }

        var localRoles = Lakona.Game.Server.Configuration.LakonaGameRuntimeOptions
            .FromConfiguration(configuration)
            .Node.Roles.ToHashSet(StringComparer.Ordinal);
        var localModuleTypes = moduleTypes
            .Where(type => localRoles.Contains(NodeRoleName.GetRequiredRole(type)))
            .ToArray();

        var registrations = localModuleTypes
            .Select(static type => new LakonaModuleRegistration(
                type,
                (ILakonaModule)Activator.CreateInstance(type)!))
            .ToArray();
        var catalog = new LakonaModuleCatalog(registrations);

        services.AddSingleton(catalog);
        services.AddSingleton<LakonaModuleRuntime>();
        foreach (var registration in registrations)
        {
            services.AddSingleton(registration.ModuleType, registration.Instance);
            services.AddSingleton(typeof(ILakonaModule), registration.Instance);
        }

        foreach (var registration in registrations)
        {
            registration.Instance.ConfigureServices(services, configuration);
        }

        return catalog;
    }

    internal static IReadOnlyList<Type> DiscoverTypes(
        IEnumerable<Assembly> assemblies,
        bool excludeTestAssemblies = true)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var candidates = assemblies
            .Where(assembly => !IsHotfixAssembly(assembly))
            .Where(assembly => !excludeTestAssemblies || !IsTestAssembly(assembly))
            .Distinct()
            .SelectMany(GetLoadableTypes)
            .Where(static type => typeof(ILakonaModule).IsAssignableFrom(type))
            .Where(static type => type != typeof(ILakonaModule))
            .ToArray();

        foreach (var candidate in candidates)
        {
            ValidateModuleType(candidate);
        }

        var duplicate = candidates
            .GroupBy(static type => type.FullName, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Lakona module identity '{duplicate.Key}' is declared by more than one stable application assembly.");
        }

        return candidates
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateModuleType(Type type)
    {
        if (!type.IsVisible
            || !type.IsClass
            || type.IsAbstract
            || !type.IsSealed
            || type.ContainsGenericParameters
            || type.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException(
                $"Lakona module '{type.FullName}' must be a public, sealed, non-generic class with a public parameterless constructor.");
        }
    }

    private static bool IsHotfixAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name;
        return name is not null
            && (name.EndsWith(".Hotfix", StringComparison.OrdinalIgnoreCase)
                || name.Contains(".Hotfix.", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTestAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name;
        return name is not null
            && (name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
                || name.Contains(".Tests.", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(static type => type is not null)!;
        }
    }
}
