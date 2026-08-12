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

    public ActorPlacementService(
        IActorDirectory actorDirectory,
        ClusterCapabilityIndex capabilityIndex,
        IActorHostClient hostClient,
        ActorHosting actorHosting,
        LocalActorNodeIdentity localNode,
        IHotfixRuntimeAccessor hotfixRuntime,
        IClusterMembership membership)
    {
        this.actorDirectory = actorDirectory ?? throw new ArgumentNullException(nameof(actorDirectory));
        this.capabilityIndex = capabilityIndex ?? throw new ArgumentNullException(nameof(capabilityIndex));
        this.hostClient = hostClient ?? throw new ArgumentNullException(nameof(hostClient));
        this.actorHosting = actorHosting;
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
            key is ActorId id ? id : ActorIdentity.Create<TActor, TKey>(key),
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
                member.State == ClusterMemberState.Ready
                && member.Reference.Node == selectedRecord.Node
                && member.ActorHosts.Any(host => string.Equals(host.Actor, actorName, StringComparison.Ordinal))).ToArray();
            if (owners.Length != 1)
            {
                throw new ActorPlacementException(
                    actorType,
                    actorId,
                    $"Selected node '{selectedRecord.Node.Value}' no longer has one exact Ready Actor capability.");
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
                await ReleaseFailedActivationAsync().ConfigureAwait(false);
                throw new ActorPlacementException(
                    actorType,
                    actorId,
                    $"Actor placement failed while activating actor id '{actorId.Value}' on local node '{localNode.NodeId.Value}'.",
                    ex);
            }
            catch
            {
                await ReleaseFailedActivationAsync().ConfigureAwait(false);
                throw;
            }

            return activation is null
                ? new ActorPlacementResult(actorId, localNode.NodeId)
                : new ActorPlacementResult(activation);
        }

        ActorHostCreateReply reply;
        try
        {
            reply = await hostClient.CreateAsync(
                selectedRecord.Node,
                new ActorHostCreateRequest(
                    actorName,
                    actorId.Value,
                    ToWireMode(createMode),
                    selectedHost.BuildTag,
                    activation?.OwnerReference?.Cluster.Value.ToString("D"),
                    activation?.OwnerReference?.Incarnation.Value.ToString("D"),
                    activation?.ActivationId?.Value.ToString("D"),
                    activation?.Version ?? 0),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await ReleaseFailedActivationAsync().ConfigureAwait(false);
            throw;
        }
        if (reply.Succeeded && !string.IsNullOrWhiteSpace(reply.OwnerNode))
        {
            return activation is null
                ? new ActorPlacementResult(actorId, new NodeId(reply.OwnerNode))
                : new ActorPlacementResult(activation);
        }

        if (!reply.Succeeded && !string.IsNullOrWhiteSpace(reply.OwnerNode))
        {
            await ReleaseFailedActivationAsync().ConfigureAwait(false);
            var owner = new NodeId(reply.OwnerNode);
            if (createMode == ActorPlacementCreateMode.Create)
            {
                throw AlreadyPlaced(actorType, actorId, owner);
            }

            return new ActorPlacementResult(actorId, owner);
        }

        await ReleaseFailedActivationAsync().ConfigureAwait(false);
        throw new ActorPlacementException(
            actorType,
            actorId,
            $"Actor host create failed on node '{selectedRecord.Node.Value}': {reply.Message}");

        async ValueTask ReleaseFailedActivationAsync()
        {
            if (!acquiredActivation
                || activation?.ActivationId is not ActorActivationId activationId
                || actorDirectory is not IActorActivationDirectory activationDirectory)
            {
                return;
            }

            await activationDirectory.ReleaseAsync(
                actorId,
                activationId,
                activation.Version,
                CancellationToken.None).ConfigureAwait(false);
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

    private static string ToWireMode(ActorPlacementCreateMode createMode)
    {
        return createMode switch
        {
            ActorPlacementCreateMode.Create => "create",
            ActorPlacementCreateMode.Ensure => "ensure",
            _ => throw new ArgumentOutOfRangeException(nameof(createMode), createMode, "Unknown actor placement create mode.")
        };
    }
}
