using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Lakona.Game.Server.Internal.ActorKernel.Core;

internal sealed class ActorRegistry
{
    private readonly ConcurrentDictionary<ActorId, ActorCell> actors = new();
    internal bool TryAdd(ActorId id, ActorCell cell)
    {
        return actors.TryAdd(id, cell);
    }

    internal bool TryGet(ActorId id, [NotNullWhen(true)] out ActorCell? cell)
    {
        return actors.TryGetValue(id, out cell);
    }

    internal void Remove(ActorId id, ActorCell cell)
    {
        RemoveExact(actors, id, cell);
    }

    internal ActorCell[] SnapshotAndClear()
    {
        ActorCell[] cells = actors.Values.ToArray();
        actors.Clear();
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
