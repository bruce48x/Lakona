using Lakona.Game.Server.Actors;

namespace Lakona.Game.Server.Hotfix;

internal sealed class StartupActorPublicationParticipant(StartupActorHostedService hostedService)
    : IHotfixRuntimePublicationParticipant
{
    public ValueTask<IHotfixRuntimePublicationTransaction> PrepareAsync(
        HotfixRuntimeSnapshot previous,
        HotfixRuntimeSnapshot candidate,
        CancellationToken cancellationToken = default) =>
        hostedService.PrepareAsync(previous, candidate, cancellationToken);
}
