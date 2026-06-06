using Lakona.Game.Server.Actors;

namespace Lakona.Game.Server.Diagnostics;

public interface IMessageLogStore
{
    ValueTask RecordAsync(ActorId actorId, MessageLogEntry entry, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<MessageLogEntry>> GetLogAsync(ActorId actorId, CancellationToken cancellationToken = default);

    ValueTask ClearAsync(ActorId actorId, CancellationToken cancellationToken = default);
}
