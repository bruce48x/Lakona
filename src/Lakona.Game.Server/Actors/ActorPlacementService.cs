using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Actors;

internal sealed class ActorPlacementService : IActorPlacementService
{
    private readonly IActorDirectory actorDirectory;
    private readonly ClusterCapabilityIndex capabilityIndex;
    private readonly IActorHostClient hostClient;
    private readonly ActorActivationCatalog activationCatalog;
    private readonly LocalActorNodeIdentity localNode;
    private readonly IHotfixRuntimeAccessor hotfixRuntime;
    private readonly IClusterMembership membership;

    public ActorPlacementService(
        IActorDirectory actorDirectory,
        ClusterCapabilityIndex capabilityIndex,
        IActorHostClient hostClient,
        ActorActivationCatalog activationCatalog,
        LocalActorNodeIdentity localNode,
        IHotfixRuntimeAccessor hotfixRuntime,
        IClusterMembership membership)
    {
        this.actorDirectory = actorDirectory ?? throw new ArgumentNullException(nameof(actorDirectory));
        this.capabilityIndex = capabilityIndex ?? throw new ArgumentNullException(nameof(capabilityIndex));
        this.hostClient = hostClient ?? throw new ArgumentNullException(nameof(hostClient));
        this.activationCatalog = activationCatalog;
        this.localNode = localNode ?? throw new ArgumentNullException(nameof(localNode));
        this.hotfixRuntime = hotfixRuntime ?? throw new ArgumentNullException(nameof(hotfixRuntime));
        this.membership = membership ?? throw new ArgumentNullException(nameof(membership));
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

        var target = new ActorLifecycleTarget(
            actorId,
            owners[0].Reference,
            ActorActivationId.New());

        if (selectedRecord.Node == localNode.NodeId)
        {
            try
            {
                await activationCatalog.ActivateExactAsync(actorType, target, createMode, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ActorHostingException ex)
            {
                throw new ActorPlacementException(
                    actorType,
                    actorId,
                    $"Actor placement failed while activating actor id '{actorId.Value}' on local node '{localNode.NodeId.Value}'.",
                    ex);
            }
            return new ActorPlacementResult(actorId, localNode.NodeId);
        }

        ActorHostCommandReply reply;
        reply = await hostClient.CreateAsync(
            new ActorHostCreateCommand(actorName, target, createMode, selectedHost.HotfixVersion),
            cancellationToken).ConfigureAwait(false);
        if (reply.Succeeded && reply.OwnerNode == target.Owner.Node)
        {
            return new ActorPlacementResult(actorId, reply.OwnerNode.Value);
        }

        if (reply.Succeeded)
        {
            throw new ActorPlacementException(
                actorType,
                actorId,
                $"Actor host create on node '{selectedRecord.Node.Value}' returned an invalid owner.");
        }

        if (!reply.Succeeded && reply.OwnerNode is { } existingOwner)
        {
            if (createMode == ActorPlacementCreateMode.Create)
            {
                throw AlreadyPlaced(actorType, actorId, existingOwner);
            }

            return new ActorPlacementResult(actorId, existingOwner);
        }

        throw new ActorPlacementException(
            actorType,
            actorId,
            $"Actor host create failed on node '{selectedRecord.Node.Value}': {reply.Message}");
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

        var owner = current.OwnerReference;
        var activation = current.ActivationId;

        if (owner.Node == localNode.NodeId)
        {
            await activationCatalog.DestroyExactAsync<TActor>(
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
