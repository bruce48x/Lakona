using System.Reflection;

namespace Lakona.Game.Server.Features;

public sealed class FeatureBuilder
{
    private readonly List<INodeRole> _roles = new();
    private FeatureFilter _filter = new();

    public FeatureBuilder UseFilter(FeatureFilter filter)
    {
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        return this;
    }

    public FeatureBuilder AddRole<TRole>() where TRole : INodeRole, new()
    {
        _roles.Add(new TRole());
        return this;
    }

    public FeatureBuilder AddRole(INodeRole role)
    {
        ArgumentNullException.ThrowIfNull(role);
        _roles.Add(role);
        return this;
    }

    public FeatureBuilder FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var type in assembly.GetTypes())
        {
            if (!typeof(INodeRole).IsAssignableFrom(type) || type.IsAbstract || type.IsInterface)
            {
                continue;
            }

            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                continue;
            }

            _roles.Add((INodeRole)Activator.CreateInstance(type)!);
        }

        return this;
    }

    public IEnumerable<IFeature> ResolveFeatures()
    {
        var activeRoles = _filter.Roles is { Length: > 0 }
            ? _roles.Where(r => _filter.Roles!.Contains(r.Name, StringComparer.OrdinalIgnoreCase))
            : _roles;

        if (_filter.Roles is { Length: > 0 })
        {
            var activeNames = activeRoles.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = _filter.Roles.Except(activeNames, StringComparer.OrdinalIgnoreCase).ToArray();

            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    $"The following roles were requested but not found: {string.Join(", ", missing)}. " +
                    $"Available roles: {string.Join(", ", _roles.Select(r => r.Name))}.");
            }
        }

        var seen = new HashSet<Type>();

        foreach (var role in activeRoles)
        {
            foreach (var feature in role.Features)
            {
                if (seen.Add(feature.GetType()))
                {
                    yield return feature;
                }
            }
        }
    }
}
