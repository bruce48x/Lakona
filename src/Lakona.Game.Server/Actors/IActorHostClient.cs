using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

internal interface IActorHostClient
{
    ValueTask<ActorHostCommandReply> CreateAsync(
        ActorHostCreateCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<ActorHostCommandReply> DestroyAsync(
        ActorHostDestroyCommand command,
        CancellationToken cancellationToken = default);
}
