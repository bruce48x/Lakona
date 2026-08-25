using Lakona.Game.Server.Actors;

namespace Lakona.Game.Server;

internal sealed class NodeRoleCatalog
{
    private readonly HashSet<string> _localRoles;
    private readonly Dictionary<string, string> _actorRoles;

    internal NodeRoleCatalog(IEnumerable<string> localRoles, IEnumerable<Type> actorTypes)
    {
        ArgumentNullException.ThrowIfNull(localRoles);
        ArgumentNullException.ThrowIfNull(actorTypes);
        _localRoles = localRoles.Select(NodeRoleName.Normalize).ToHashSet(StringComparer.Ordinal);
        _actorRoles = actorTypes.ToDictionary(
            ActorNameResolver.Resolve,
            NodeRoleName.GetRequiredRole,
            StringComparer.Ordinal);
    }

    internal bool IsLocal(Type stableType) =>
        _localRoles.Contains(NodeRoleName.GetRequiredRole(stableType));

    internal bool IsLocalActor(string actorName) =>
        _actorRoles.TryGetValue(actorName, out var role) && _localRoles.Contains(role);

    internal IReadOnlyList<string> LocalActorNames => _actorRoles
        .Where(pair => _localRoles.Contains(pair.Value))
        .Select(static pair => pair.Key)
        .OrderBy(static name => name, StringComparer.Ordinal)
        .ToArray();
}
