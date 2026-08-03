using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Hotfix.Loading;

internal sealed class HotfixAssemblyLoadContext : AssemblyLoadContext
{
    private static readonly string RuntimeAssemblyName = typeof(IGameSessionLifecycle).Assembly.GetName().Name!;
    private static readonly string DependencyInjectionAbstractionsAssemblyName = typeof(IServiceCollection).Assembly.GetName().Name!;
    private static readonly string LoggingAbstractionsAssemblyName = typeof(ILogger).Assembly.GetName().Name!;

    private readonly AssemblyDependencyResolver _resolver;
    private readonly IReadOnlySet<string> _hostAssemblyNames;
    private readonly IReadOnlyDictionary<string, Assembly> _hostAssemblies;

    public HotfixAssemblyLoadContext(string mainAssemblyPath, IEnumerable<string> hostAssemblyNames)
        : base("Lakona.Game.Hotfix", isCollectible: true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mainAssemblyPath);

        _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        (_hostAssemblyNames, _hostAssemblies) = CreateHostAssemblyPolicy(hostAssemblyNames);
    }

    public Assembly LoadMainAssemblyFromBytes(string assemblyPath)
    {
        return LoadAssemblyFromBytes(assemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is not null && _hostAssemblyNames.Contains(assemblyName.Name))
        {
            return _hostAssemblies.TryGetValue(assemblyName.Name, out var hostAssembly)
                ? hostAssembly
                : throw new FileNotFoundException($"Host assembly '{assemblyName.Name}' is not loaded in the default AssemblyLoadContext.");
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadAssemblyFromBytes(path);
    }

    private Assembly LoadAssemblyFromBytes(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);

        using var assemblyStream = new MemoryStream(File.ReadAllBytes(assemblyPath));
        var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        if (File.Exists(pdbPath))
        {
            using var pdbStream = new MemoryStream(File.ReadAllBytes(pdbPath));
            return LoadFromStream(assemblyStream, pdbStream);
        }

        return LoadFromStream(assemblyStream);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }

    private static (IReadOnlySet<string> Names, IReadOnlyDictionary<string, Assembly> Assemblies) CreateHostAssemblyPolicy(IEnumerable<string> hostAssemblyNames)
    {
        ArgumentNullException.ThrowIfNull(hostAssemblyNames);

        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            RuntimeAssemblyName,
            DependencyInjectionAbstractionsAssemblyName,
            LoggingAbstractionsAssemblyName
        };

        foreach (var name in hostAssemblyNames)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        var assemblies = Default.Assemblies
            .Where(assembly => assembly.GetName().Name is { } name && names.Contains(name))
            .ToDictionary(assembly => assembly.GetName().Name!, StringComparer.Ordinal);

        return (names, assemblies);
    }
}
