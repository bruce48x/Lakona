using System.Text.RegularExpressions;

namespace Lakona.Game.Server.Hosting;

public sealed class LakonaRpcServiceCatalog
{
    private static readonly Regex ServiceNamePattern = new(
        "^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$",
        RegexOptions.CultureInvariant);

    private readonly Dictionary<string, LakonaRpcServiceDescriptor> _byName;

    private LakonaRpcServiceCatalog(Dictionary<string, LakonaRpcServiceDescriptor> byName)
    {
        _byName = byName;
    }

    public static LakonaRpcServiceCatalog FromTypes(IReadOnlyList<Type> binderTypes)
    {
        ArgumentNullException.ThrowIfNull(binderTypes);

        var byName = new Dictionary<string, LakonaRpcServiceDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var binderType in binderTypes)
        {
            if (!typeof(LakonaRpcServiceBinder).IsAssignableFrom(binderType))
            {
                throw new InvalidOperationException(
                    $"RPC service binder type '{binderType.FullName}' must inherit {nameof(LakonaRpcServiceBinder)}.");
            }

            var attributes = binderType.GetCustomAttributes(typeof(LakonaRpcServiceAttribute), inherit: false)
                .Cast<LakonaRpcServiceAttribute>()
                .ToArray();
            if (attributes.Length != 1)
            {
                throw new InvalidOperationException(
                    $"RPC service binder type '{binderType.FullName}' must declare exactly one {nameof(LakonaRpcServiceAttribute)}.");
            }

            var name = attributes[0].Name.ToLowerInvariant();
            if (!ServiceNamePattern.IsMatch(attributes[0].Name))
            {
                throw new InvalidOperationException(
                    $"RPC service name '{attributes[0].Name}' on '{binderType.FullName}' must be lower-case kebab-case.");
            }

            if (byName.ContainsKey(name))
            {
                throw new InvalidOperationException($"RPC service name '{name}' is already registered.");
            }

            byName.Add(name, new LakonaRpcServiceDescriptor(name, binderType));
        }

        return new LakonaRpcServiceCatalog(byName);
    }

    public bool TryGet(string name, out LakonaRpcServiceDescriptor descriptor)
    {
        if (_byName.TryGetValue(name, out var found))
        {
            descriptor = found;
            return true;
        }

        descriptor = null!;
        return false;
    }
}

public sealed record LakonaRpcServiceDescriptor(string Name, Type BinderType);
