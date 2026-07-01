using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix;

public sealed class HotfixLocalActorPublicationParticipant(IActorLifecycle actorLifecycle) : IHotfixRuntimePublicationParticipant
{
    public ValueTask BeforePublishAsync(
        HotfixRuntimeSnapshot candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();

        var actorTypesById = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var actor in EnumerateLocalActors(candidate))
        {
            ValidateLocalActorDeclaration(actor, actorTypesById);
        }

        return default;
    }

    public async ValueTask AfterPublishAsync(
        HotfixRuntimeSnapshot previous,
        HotfixRuntimeSnapshot current,
        CancellationToken cancellationToken = default)
    {
        _ = previous;
        ArgumentNullException.ThrowIfNull(current);

        foreach (var actor in EnumerateLocalActors(current))
        {
            await CreateLocalActorAsync(actor, cancellationToken).ConfigureAwait(false);
        }
    }

    private static IEnumerable<HotfixLocalActorDeclaration> EnumerateLocalActors(HotfixRuntimeSnapshot snapshot)
    {
        return snapshot.DispatchTable?.Features.SelectMany(static feature => feature.LocalActors)
            ?? Array.Empty<HotfixLocalActorDeclaration>();
    }

    private static void ValidateLocalActorDeclaration(
        HotfixLocalActorDeclaration declaration,
        Dictionary<string, Type> actorTypesById)
    {
        if (!typeof(IActor).IsAssignableFrom(declaration.ActorType))
        {
            throw new InvalidOperationException(
                $"Hotfix local actor type '{declaration.ActorType.FullName}' must implement {typeof(IActor).FullName}.");
        }

        if (actorTypesById.TryGetValue(declaration.ActorId, out var existingType) &&
            existingType != declaration.ActorType)
        {
            throw new InvalidOperationException(
                $"Hotfix local actor id '{declaration.ActorId}' is declared for both '{existingType.FullName}' and '{declaration.ActorType.FullName}'.");
        }

        actorTypesById[declaration.ActorId] = declaration.ActorType;
    }

    private async ValueTask CreateLocalActorAsync(
        HotfixLocalActorDeclaration declaration,
        CancellationToken cancellationToken)
    {
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
