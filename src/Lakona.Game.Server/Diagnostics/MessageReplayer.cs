using Lakona.Game.Server.Actors;

namespace Lakona.Game.Server.Diagnostics;

public sealed class MessageReplayer
{
    private readonly IMessageLogStore _store;
    private readonly IActorRuntime _runtime;

    public MessageReplayer(IMessageLogStore store, IActorRuntime runtime)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public async ValueTask<int> ReplayAsync<TActor>(
        ActorId actorId,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor
    {
        var log = await _store.GetLogAsync(actorId, cancellationToken).ConfigureAwait(false);
        var replayed = 0;

        foreach (var entry in log)
        {
            try
            {
                await _runtime.TellAsync<TActor>(
                    actorId,
                    (_, ct) => ValueTask.CompletedTask,
                    cancellationToken).ConfigureAwait(false);

                replayed++;
            }
            catch (Exception)
            {
                break;
            }
        }

        return replayed;
    }
}
