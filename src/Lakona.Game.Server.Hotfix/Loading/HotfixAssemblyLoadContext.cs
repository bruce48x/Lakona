using System.Reflection;
using System.Runtime.Loader;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix.Loading;

internal sealed class HotfixAssemblyLoadContext : AssemblyLoadContext
{
    private static readonly string AbstractionsAssemblyName = typeof(HotfixSystemOfAttribute).Assembly.GetName().Name!;

    private readonly AssemblyDependencyResolver _resolver;
    private readonly IReadOnlySet<string> _sharedAssemblyNames;
    private readonly IReadOnlyDictionary<string, Assembly> _sharedAssemblies;

    public HotfixAssemblyLoadContext(string mainAssemblyPath, IEnumerable<string> sharedAssemblyNames)
        : base("Lakona.Game.Hotfix", isCollectible: true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mainAssemblyPath);

        _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        (_sharedAssemblyNames, _sharedAssemblies) = CreateSharedAssemblyPolicy(sharedAssemblyNames);
    }

    public Assembly LoadMainAssemblyFromBytes(string assemblyPath)
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

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is not null && _sharedAssemblyNames.Contains(assemblyName.Name))
        {
            return _sharedAssemblies.TryGetValue(assemblyName.Name, out var sharedAssembly)
                ? sharedAssembly
                : throw new FileNotFoundException($"Shared assembly '{assemblyName.Name}' is not loaded in the default AssemblyLoadContext.");
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }

    private static (IReadOnlySet<string> Names, IReadOnlyDictionary<string, Assembly> Assemblies) CreateSharedAssemblyPolicy(IEnumerable<string> sharedAssemblyNames)
    {
        ArgumentNullException.ThrowIfNull(sharedAssemblyNames);

        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            AbstractionsAssemblyName
        };

        foreach (var name in sharedAssemblyNames)
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
