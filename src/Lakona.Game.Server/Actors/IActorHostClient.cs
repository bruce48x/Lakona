using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public interface IActorHostClient
{
    ValueTask<ActorHostCreateReply> CreateAsync(
        NodeId node,
        ActorHostCreateRequest request,
        CancellationToken cancellationToken = default);
}
