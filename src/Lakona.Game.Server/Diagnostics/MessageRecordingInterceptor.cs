using System.Collections.Concurrent;
using Lakona.Game.Server.Actors;

namespace Lakona.Game.Server.Diagnostics;

public sealed class MessageRecordingInterceptor : global::Lakona.Actor.IActorMessageInterceptor
{
    private readonly IMessageLogStore _store;
    private readonly ConcurrentDictionary<global::Lakona.Actor.ActorId, ActorId> _idMap;

    public MessageRecordingInterceptor(
        IMessageLogStore store,
        ConcurrentDictionary<global::Lakona.Actor.ActorId, ActorId> idMap)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _idMap = idMap ?? throw new ArgumentNullException(nameof(idMap));
    }

    public ValueTask OnBeforeMessage(
        global::Lakona.Actor.ActorId actorId,
        object message,
        CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask OnAfterMessage(
        global::Lakona.Actor.ActorId actorId,
        object message,
        Exception? error,
        CancellationToken cancellationToken)
    {
        var gameId = _idMap.TryGetValue(actorId, out var mapped)
            ? mapped
            : ActorId.From(actorId.Value.ToString());

        var entry = new MessageLogEntry(
            DateTimeOffset.UtcNow,
            message,
            error?.GetType().FullName);

        await _store.RecordAsync(gameId, entry, cancellationToken).ConfigureAwait(false);
    }
}
