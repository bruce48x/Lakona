using System.Collections.Concurrent;
using Lakona.Game.Server.Actors;

namespace Lakona.Game.Server.Diagnostics;

public sealed class MessageRecordingInterceptor : IActorMessageInterceptor
{
    private readonly IMessageLogStore _store;
    private readonly ConcurrentDictionary<ActorId, ActorId> _idMap;

    public MessageRecordingInterceptor(
        IMessageLogStore store,
        ConcurrentDictionary<ActorId, ActorId> idMap)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _idMap = idMap ?? throw new ArgumentNullException(nameof(idMap));
    }

    public ValueTask OnBeforeMessage(
        ActorId actorId,
        string messageType,
        object? message,
        CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask OnAfterMessage(
        ActorId actorId,
        string messageType,
        object? message,
        Exception? error,
        CancellationToken cancellationToken)
    {
        var gameId = _idMap.TryGetValue(actorId, out var mapped)
            ? mapped
            : actorId;

        var entry = new MessageLogEntry(
            DateTimeOffset.UtcNow,
            message,
            error?.GetType().FullName);

        await _store.RecordAsync(gameId, entry, cancellationToken).ConfigureAwait(false);
    }
}
