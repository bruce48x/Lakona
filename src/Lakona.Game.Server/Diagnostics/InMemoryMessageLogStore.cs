using System.Collections.Concurrent;
using Lakona.Game.Server.Actors;

namespace Lakona.Game.Server.Diagnostics;

public sealed class InMemoryMessageLogStore : IMessageLogStore
{
    private readonly ConcurrentDictionary<ActorId, List<MessageLogEntry>> _logs = new();
    private readonly int _maxEntriesPerActor;

    public InMemoryMessageLogStore(int maxEntriesPerActor = 4096)
    {
        if (maxEntriesPerActor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntriesPerActor));
        }

        _maxEntriesPerActor = maxEntriesPerActor;
    }

    public ValueTask RecordAsync(ActorId actorId, MessageLogEntry entry, CancellationToken cancellationToken = default)
    {
        var list = _logs.GetOrAdd(actorId, _ => new List<MessageLogEntry>());

        lock (list)
        {
            if (list.Count >= _maxEntriesPerActor)
            {
                list.RemoveAt(0);
            }

            list.Add(entry);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<MessageLogEntry>> GetLogAsync(ActorId actorId, CancellationToken cancellationToken = default)
    {
        if (_logs.TryGetValue(actorId, out var list))
        {
            lock (list)
            {
                return ValueTask.FromResult<IReadOnlyList<MessageLogEntry>>(list.ToArray());
            }
        }

        return ValueTask.FromResult<IReadOnlyList<MessageLogEntry>>(Array.Empty<MessageLogEntry>());
    }

    public ValueTask ClearAsync(ActorId actorId, CancellationToken cancellationToken = default)
    {
        _logs.TryRemove(actorId, out _);
        return ValueTask.CompletedTask;
    }
}
