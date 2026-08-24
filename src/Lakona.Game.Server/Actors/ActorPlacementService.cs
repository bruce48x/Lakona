using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Actors;

internal sealed class ActorPlacementService : IActorPlacementService
{
    private readonly IActorDirectory actorDirectory;
    private readonly ClusterCapabilityIndex capabilityIndex;
    private readonly IActorHostClient hostClient;
    private readonly ActorHosting actorHosting;
    private readonly LocalActorNodeIdentity localNode;
    private readonly IHotfixRuntimeAccessor hotfixRuntime;
    private readonly IClusterMembership membership;
    private readonly ActorCompensationLifetime compensationLifetime;

    public ActorPlacementService(
        IActorDirectory actorDirectory,
        ClusterCapabilityIndex capabilityIndex,
        IActorHostClient hostClient,
        ActorHosting actorHosting,
        LocalActorNodeIdentity localNode,
        IHotfixRuntimeAccessor hotfixRuntime,
        IClusterMembership membership,
        ActorCompensationLifetime? compensationLifetime = null)
    {
        this.actorDirectory = actorDirectory ?? throw new ArgumentNullException(nameof(actorDirectory));
        this.capabilityIndex = capabilityIndex ?? throw new ArgumentNullException(nameof(capabilityIndex));
        this.hostClient = hostClient ?? throw new ArgumentNullException(nameof(hostClient));
        this.actorHosting = actorHosting;
        this.localNode = localNode ?? throw new ArgumentNullException(nameof(localNode));
        this.hotfixRuntime = hotfixRuntime ?? throw new ArgumentNullException(nameof(hotfixRuntime));
        this.membership = membership ?? throw new ArgumentNullException(nameof(membership));
        this.compensationLifetime = compensationLifetime ?? new ActorCompensationLifetime();
    }

    public async ValueTask<ActorPlacementResult> PlaceAsync<TActor, TKey>(
        TKey key,
        ActorPlacementCreateMode createMode,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor
        where TKey : notnull =>
        await PlaceAsync<TActor, TKey>(
            ActorIdentity.CreateOrUseExact<TActor, TKey>(key),
            key,
            createMode,
            cancellationToken).ConfigureAwait(false);

    public async ValueTask<ActorPlacementResult> PlaceAsync<TActor, TKey>(
        ActorId actorId,
        TKey key,
        ActorPlacementCreateMode createMode,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor
        where TKey : notnull
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actorType = typeof(TActor);

        var existing = await actorDirectory.ResolveAsync(actorId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (createMode == ActorPlacementCreateMode.Create)
            {
                throw AlreadyPlaced(actorType, actorId, existing.Node);
            }

            return new ActorPlacementResult(existing);
        }

        var selector = ResolvePlacementSelector<TActor, TKey>(actorType, actorId);
        var actorName = ActorNameResolver.Resolve(actorType);
        var records = capabilityIndex.FindReadyActorHosts(actorName);
        var candidates = records
            .Select(record => new ActorHostCandidate(
                record.Node.Value,
                record.Host.Metadata))
            .ToArray();
        if (candidates.Length == 0)
        {
            throw new ActorPlacementException(
                actorType,
                actorId,
                $"No actor host candidates are available for actor '{actorName}'.");
        }

        ActorHostCandidate selected;
        try
        {
            selected = selector(new ActorPlacementContext<TKey>(candidates, key));
        }
        catch (Exception ex)
        {
            throw new ActorPlacementException(
                actorType,
                actorId,
                $"Actor placement selector for '{actorType.FullName}' failed.",
                ex);
        }

        var selectedRecord = records.FirstOrDefault(record =>
            string.Equals(record.Node.Value, selected.NodeId, StringComparison.Ordinal));
        if (selectedRecord is null)
        {
            throw new ActorPlacementException(
                actorType,
                actorId,
                $"Actor placement selector returned node '{selected.NodeId}', which is not one of the discovered candidates.");
        }

        var selectedHost = selectedRecord.Host;
        ActorDirectoryRecord? activation = null;
        var acquiredActivation = false;
        if (actorDirectory is IActorActivationDirectory activationDirectory)
        {
            var snapshot = membership.Current;
            var owners = snapshot.Members.Where(member =>
                member.State == ClusterMemberState.Active
                && member.Reference.Node == selectedRecord.Node
                && member.ActorHosts.Any(host => string.Equals(host.Actor, actorName, StringComparison.Ordinal))).ToArray();
            if (owners.Length != 1)
            {
                throw new ActorPlacementException(
                    actorType,
                    actorId,
                    $"Selected node '{selectedRecord.Node.Value}' no longer has one exact Active Actor capability.");
            }

            var acquired = await activationDirectory.AcquireAsync(
                actorId,
                owners[0].Reference,
                ActorActivationId.New(),
                cancellationToken).ConfigureAwait(false);
            activation = acquired.Record;
            acquiredActivation = acquired.Acquired;
            if (!acquired.Acquired)
            {
                if (createMode == ActorPlacementCreateMode.Create)
                {
                    throw AlreadyPlaced(actorType, actorId, acquired.Record.Node);
                }

                return new ActorPlacementResult(acquired.Record);
            }
        }

        if (selectedRecord.Node == localNode.NodeId)
        {
            try
            {
                if (createMode == ActorPlacementCreateMode.Create)
                {
                    await actorHosting.CreateAsync<TActor>(actorId, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await actorHosting.EnsureAsync<TActor>(actorId, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (ActorHostingException ex)
            {
                await ReleaseFailedActivationAsync(ex).ConfigureAwait(false);
                throw new ActorPlacementException(
                    actorType,
                    actorId,
                    $"Actor placement failed while activating actor id '{actorId.Value}' on local node '{localNode.NodeId.Value}'.",
                    ex);
            }
            catch (Exception ex)
            {
                await ReleaseFailedActivationAsync(ex).ConfigureAwait(false);
                throw;
            }

            return activation is null
                ? new ActorPlacementResult(actorId, localNode.NodeId)
                : new ActorPlacementResult(activation);
        }

        if (activation?.OwnerReference is not { } activationOwner
            || activation.ActivationId is not { } activationId)
        {
            throw new ActorPlacementException(
                actorType,
                actorId,
                "Remote Actor placement requires one exact activation identity.");
        }

        ActorHostCommandReply reply;
        try
        {
            reply = await hostClient.CreateAsync(
                new ActorHostCreateCommand(
                    actorName,
                    new ActorLifecycleTarget(actorId, activationOwner, activationId),
                    createMode,
                    selectedHost.BuildTag),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await ReleaseFailedActivationAsync(ex).ConfigureAwait(false);
            throw;
        }
        if (reply.Succeeded && reply.OwnerNode is not null)
        {
            return new ActorPlacementResult(activation);
        }

        if (!reply.Succeeded && reply.OwnerNode is { } existingOwner)
        {
            await ReleaseFailedActivationAsync().ConfigureAwait(false);
            if (createMode == ActorPlacementCreateMode.Create)
            {
                throw AlreadyPlaced(actorType, actorId, existingOwner);
            }

            return new ActorPlacementResult(actorId, existingOwner);
        }

        await ReleaseFailedActivationAsync().ConfigureAwait(false);
        throw new ActorPlacementException(
            actorType,
            actorId,
            $"Actor host create failed on node '{selectedRecord.Node.Value}': {reply.Message}");

        async ValueTask ReleaseFailedActivationAsync(Exception? activationFailure = null)
        {
            if (!acquiredActivation
                || activation?.ActivationId is not ActorActivationId activationId
                || actorDirectory is not IActorActivationDirectory activationDirectory)
            {
                return;
            }

            try
            {
                await compensationLifetime.ExecuteAsync(
                    actorId,
                    "failed-placement activation release",
                    async cleanupToken =>
                    {
                        var released = await activationDirectory.ReleaseAsync(
                            actorId,
                            activationId,
                            cleanupToken).ConfigureAwait(false);
                        if (!released)
                            throw new ActorDirectoryUnavailableException(
                                $"Actor directory did not confirm release of failed activation '{activationId.Value:D}' for '{actorId.Value}'.");
                    }).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                var cause = activationFailure is null
                    ? exception
                    : new AggregateException(activationFailure, exception);
                throw new ActorPlacementException(
                    actorType,
                    actorId,
                    $"Actor placement failed and activation compensation for actor id '{actorId.Value}' is unconfirmed.",
                    cause);
            }
        }
    }

    public async ValueTask DestroyAsync<TActor>(
        ActorId actorId,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor
    {
        cancellationToken.ThrowIfCancellationRequested();

        var current = await actorDirectory.ResolveAsync(actorId, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return;
        }

        if (current.OwnerReference is not { } owner || current.ActivationId is not { } activation)
        {
            throw new ActorPlacementException(
                typeof(TActor),
                actorId,
                $"Actor id '{actorId.Value}' does not have an exact activation identity and cannot be destroyed safely.");
        }

        if (owner.Node == localNode.NodeId)
        {
            await actorHosting.DestroyExactAsync<TActor>(
                actorId,
                owner,
                activation,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var reply = await hostClient.DestroyAsync(
            new ActorHostDestroyCommand(
                ActorNameResolver.Resolve(typeof(TActor)),
                new ActorLifecycleTarget(actorId, owner, activation)),
            cancellationToken).ConfigureAwait(false);
        if (!reply.Succeeded)
        {
            throw new ActorPlacementException(
                typeof(TActor),
                actorId,
                $"Actor host destroy failed on node '{owner.Node.Value}': {reply.Message}");
        }
    }

    private static ActorPlacementException AlreadyPlaced(
        Type actorType,
        ActorId actorId,
        NodeId owner)
    {
        return new ActorPlacementException(
            actorType,
            actorId,
            $"Actor id '{actorId.Value}' already has an activation owned by node '{owner.Value}'.");
    }

    private Func<ActorPlacementContext<TKey>, ActorHostCandidate> ResolvePlacementSelector<TActor, TKey>(
        Type actorType,
        ActorId actorId)
        where TActor : class, IActor
    {
        using var lease = hotfixRuntime.AcquireCurrent();
        var placement = lease.Snapshot.ActorPlacements
            .FirstOrDefault(item => item.ActorType == actorType);
        if (placement is null)
        {
            return ActorPlacementSelectors.Rendezvous;
        }

        if (placement.KeyType != typeof(TKey))
        {
            throw new ActorPlacementException(
                actorType,
                actorId,
                $"Actor placement selector for '{actorType.FullName}' expects key type '{placement.KeyType.FullName}', not '{typeof(TKey).FullName}'.");
        }

        return (Func<ActorPlacementContext<TKey>, ActorHostCandidate>)placement.Selector;
    }

}
