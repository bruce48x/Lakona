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

    public HotfixAssemblyLoadContext(string mainAssemblyPath, IEnumerable<string> hostAssemblyNames)
        : base("Lakona.Game.Hotfix", isCollectible: true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mainAssemblyPath);

        _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        _hostAssemblyNames = CreateHostAssemblyPolicy(hostAssemblyNames);
    }

    public Assembly LoadMainAssemblyFromBytes(string assemblyPath)
    {
        return LoadAssemblyFromBytes(assemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is { } name
            && (_hostAssemblyNames.Contains(name)
                || Default.Assemblies.Any(assembly => StringComparer.OrdinalIgnoreCase.Equals(assembly.GetName().Name, name))))
        {
            // Let the host load lazy dependencies and enforce assembly version compatibility.
            // Never fall back to a private copy when a host-owned dependency cannot load.
            return Default.LoadFromAssemblyName(assemblyName);
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

    private static IReadOnlySet<string> CreateHostAssemblyPolicy(IEnumerable<string> hostAssemblyNames)
    {
        ArgumentNullException.ThrowIfNull(hostAssemblyNames);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            RuntimeAssemblyName,
            DependencyInjectionAbstractionsAssemblyName,
            LoggingAbstractionsAssemblyName
        };

        // The default context's runtime asset list includes the host dependency closure,
        // including assemblies which have not yet been loaded. It excludes Hotfix-only assets.
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string hostAssets)
        {
            foreach (var path in hostAssets.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                names.Add(Path.GetFileNameWithoutExtension(path));
            }
        }

        foreach (var name in hostAssemblyNames)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        return names;
    }
}
