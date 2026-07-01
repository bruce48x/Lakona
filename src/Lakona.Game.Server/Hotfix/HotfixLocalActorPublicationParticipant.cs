using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix;

public sealed class HotfixLocalActorPublicationParticipant(IActorLifecycle actorLifecycle) : IHotfixRuntimePublicationParticipant
{
    private readonly object gate = new();
    private readonly Dictionary<HotfixRuntimeSnapshot, List<PreparedLocalActor>> preparedActors = [];

    public async ValueTask BeforePublishAsync(
        HotfixRuntimeSnapshot candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();

        var localActors = EnumerateLocalActors(candidate).ToArray();
        if (localActors.Length != 0 && actorLifecycle is not IActorRuntime)
        {
            throw new InvalidOperationException(
                $"Hotfix local actor publication requires {typeof(IActorLifecycle).FullName} to also implement {typeof(IActorRuntime).FullName} so prepared actors can be rolled back before publication.");
        }

        var prepared = new List<PreparedLocalActor>();
        var actorTypesById = new Dictionary<string, Type>(StringComparer.Ordinal);
        try
        {
            foreach (var actor in localActors)
            {
                ValidateLocalActorDeclaration(actor, actorTypesById);
                var result = await CreateLocalActorAsync(actor, cancellationToken).ConfigureAwait(false);
                if (result.Status == ActorCreateLocalStatus.Created)
                {
                    prepared.Add(new PreparedLocalActor(ActorId.From(actor.ActorId)));
                }
            }
        }
        catch
        {
            await RollbackPreparedActorsAsync(prepared, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        if (prepared.Count != 0)
        {
            lock (gate)
            {
                preparedActors[candidate] = prepared;
            }
        }

        return;
    }

    public async ValueTask RollbackPublishAsync(
        HotfixRuntimeSnapshot candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        List<PreparedLocalActor>? prepared;
        lock (gate)
        {
            if (!preparedActors.Remove(candidate, out prepared))
            {
                return;
            }
        }

        await RollbackPreparedActorsAsync(prepared, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask AfterPublishAsync(
        HotfixRuntimeSnapshot previous,
        HotfixRuntimeSnapshot current,
        CancellationToken cancellationToken = default)
    {
        _ = previous;
        ArgumentNullException.ThrowIfNull(current);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            preparedActors.Remove(current);
        }

        return default;
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

    private async ValueTask<ActorCreateLocalResult> CreateLocalActorAsync(
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

        return result;
    }

    private async ValueTask RollbackPreparedActorsAsync(
        IReadOnlyList<PreparedLocalActor> prepared,
        CancellationToken cancellationToken)
    {
        if (actorLifecycle is not IActorRuntime actorRuntime)
        {
            return;
        }

        for (var index = prepared.Count - 1; index >= 0; index--)
        {
            try
            {
                await actorRuntime.StopAsync(prepared[index].ActorId).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private sealed record PreparedLocalActor(ActorId ActorId);
}
