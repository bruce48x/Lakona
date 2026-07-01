using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix;

public sealed class HotfixLocalActorPublicationParticipant(IActorLifecycle actorLifecycle) : IHotfixRuntimePublicationParticipant
{
    public async ValueTask BeforePublishAsync(
        HotfixRuntimeSnapshot candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        foreach (var actor in candidate.DispatchTable?.Features.SelectMany(static feature => feature.LocalActors)
                     ?? Array.Empty<HotfixLocalActorDeclaration>())
        {
            await CreateLocalActorAsync(actor, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask CreateLocalActorAsync(
        HotfixLocalActorDeclaration declaration,
        CancellationToken cancellationToken)
    {
        if (!typeof(IActor).IsAssignableFrom(declaration.ActorType))
        {
            throw new InvalidOperationException(
                $"Hotfix local actor type '{declaration.ActorType.FullName}' must implement {typeof(IActor).FullName}.");
        }

        var result = await actorLifecycle
            .CreateLocalAsync(declaration.ActorType, ActorId.From(declaration.ActorId), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Diagnostic ??
                $"Hotfix local actor '{declaration.ActorId}' could not be created as '{declaration.ActorType.FullName}'.");
        }
    }
}
