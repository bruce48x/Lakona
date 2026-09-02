using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

internal sealed record StartupActorAffinityRecord(
    ActorId AffinityId,
    NodeReference Target,
    long Generation,
    bool Pending = false);

internal interface IStartupActorAffinityDirectory
{
    ValueTask<StartupActorAffinityRecord?> LookupAsync(
        ActorId id,
        CancellationToken cancellationToken);

    ValueTask<StartupActorAffinityRecord> BindAsync(
        ActorId id,
        NodeReference target,
        string actorName,
        string policyHash,
        CancellationToken cancellationToken);
}
