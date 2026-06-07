using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Lakona.Game.Server.Internal.ActorKernel.Core;

internal sealed class ActorRegistry
{
    private readonly ConcurrentDictionary<ActorId, ActorCell> actors = new();
    private readonly ConcurrentDictionary<string, ActorId> names = new(StringComparer.Ordinal);

    internal bool TryAdd(ActorId id, ActorCell cell)
    {
        return actors.TryAdd(id, cell);
    }

    internal bool TryAddName(string name, ActorId id)
    {
        return names.TryAdd(name, id);
    }

    internal bool TryGet(ActorId id, [NotNullWhen(true)] out ActorCell? cell)
    {
        return actors.TryGetValue(id, out cell);
    }

    internal bool TryGetNamed(string name, out ActorId id)
    {
        if (names.TryGetValue(name, out id) && actors.ContainsKey(id))
        {
            return true;
        }

        id = default;
        return false;
    }

    internal bool TryGetNamed(string name, out ActorId id, [NotNullWhen(true)] out ActorCell? cell)
    {
        if (names.TryGetValue(name, out id) && actors.TryGetValue(id, out cell))
        {
            return true;
        }

        id = default;
        cell = null;
        return false;
    }

    internal void Remove(ActorId id, ActorCell cell)
    {
        RemoveExact(actors, id, cell);

        if (cell.Name is not null)
        {
            RemoveExact(names, cell.Name, id);
        }
    }

    internal ActorCell[] SnapshotAndClear()
    {
        ActorCell[] cells = actors.Values.ToArray();
        actors.Clear();
        names.Clear();
        return cells;
    }

    private static void RemoveExact<TKey, TValue>(
        ConcurrentDictionary<TKey, TValue> dictionary,
        TKey key,
        TValue value)
        where TKey : notnull
    {
        ((ICollection<KeyValuePair<TKey, TValue>>)dictionary).Remove(new KeyValuePair<TKey, TValue>(key, value));
    }
}
