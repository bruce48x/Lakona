using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using System.Reflection;

namespace Lakona.Game.Server.Sessions;

/// <summary>Owns stable lifecycle selections for local sessions across Hotfix generations.</summary>
public sealed class GameSessionLifecycleBindings
{
    private readonly Lock _gate = new();
    private readonly Dictionary<GameSessionKey, string> _bindings = new();
    private readonly Dictionary<string, int> _usage = new(StringComparer.Ordinal);
    private HashSet<string> _available = new(StringComparer.Ordinal);

    internal static string Identity(Type implementation)
    {
        if (!implementation.IsClass || implementation.IsAbstract || implementation.IsGenericType ||
            !typeof(IGameSessionLifecycle).IsAssignableFrom(implementation) ||
            implementation.GetCustomAttribute<HotfixLifecycleAttribute>() is null)
            throw new InvalidOperationException($"Session lifecycle '{implementation.FullName}' must be a non-generic concrete [HotfixLifecycle] class implementing IGameSessionLifecycle.");
        return $"{implementation.Assembly.GetName().Name}:{implementation.FullName}";
    }

    internal void Bind(GameSessionKey session, string identity)
    {
        lock (_gate)
        {
            if (!_available.Contains(identity))
                throw new InvalidOperationException($"Session lifecycle '{identity}' is not available in the published Hotfix generation.");
            if (!_bindings.TryAdd(session, identity))
                throw new InvalidOperationException($"Session '{session}' already has a lifecycle binding.");
            _usage[identity] = _usage.GetValueOrDefault(identity) + 1;
        }
    }

    internal void Remove(GameSessionKey session)
    {
        lock (_gate)
        {
            if (!_bindings.Remove(session, out var identity)) return;
            if (_usage[identity] == 1) _usage.Remove(identity);
            else _usage[identity]--;
        }
    }

    internal HotfixRuntimeSnapshotLease? Acquire(GameSessionKey session, IHotfixRuntimeAccessor runtime, out IGameSessionLifecycle? lifecycle)
    {
        lock (_gate)
        {
            lifecycle = null;
            if (!_bindings.TryGetValue(session, out var identity)) return null;
            var lease = runtime.AcquireCurrent();
            try
            {
                lifecycle = lease.GetSessionLifecycle(identity);
                return lease;
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }
    }

    internal void Validate(IEnumerable<string> identities)
    {
        lock (_gate) ValidateCore(identities.ToHashSet(StringComparer.Ordinal));
    }

    internal void Publish(IEnumerable<string> identities, Action publish)
    {
        lock (_gate)
        {
            var next = identities.ToHashSet(StringComparer.Ordinal);
            ValidateCore(next);
            publish();
            _available = next;
        }
    }

    private void ValidateCore(HashSet<string> available)
    {
        var missing = _usage.Keys.Where(id => !available.Contains(id)).Order().ToArray();
        if (missing.Length != 0)
            throw new InvalidOperationException($"Cannot remove or rename session lifecycle handlers while sessions or expiration callbacks still depend on them: {string.Join(", ", missing)}.");
    }
}
