using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Actors;

public sealed class ActorPlacementService : IActorPlacementService
{
    private const string ClusterName = "local";
    private readonly IActorDirectory actorDirectory;
    private readonly INodeDirectory nodeDirectory;
    private readonly IActorHostClient hostClient;
    private readonly ActorHosting actorHosting;
    private readonly LocalActorNodeIdentity localNode;
    private readonly IHotfixRuntimeAccessor hotfixRuntime;
    private readonly IClusterMembership? membership;

    [ActivatorUtilitiesConstructor]
    public ActorPlacementService(
        IActorDirectory actorDirectory,
        INodeDirectory nodeDirectory,
        IActorHostClient hostClient,
        ActorHosting actorHosting,
        LocalActorNodeIdentity localNode,
        IHotfixRuntimeAccessor hotfixRuntime)
        : this(
            actorDirectory,
            nodeDirectory,
            hostClient,
            actorHosting,
            localNode,
            hotfixRuntime,
            null)
    {
    }

    public ActorPlacementService(
        IActorDirectory actorDirectory,
        INodeDirectory nodeDirectory,
        IActorHostClient hostClient,
        ActorHosting actorHosting,
        LocalActorNodeIdentity localNode,
        IHotfixRuntimeAccessor hotfixRuntime,
        IClusterMembership? membership)
    {
        this.actorDirectory = actorDirectory ?? throw new ArgumentNullException(nameof(actorDirectory));
        this.nodeDirectory = nodeDirectory ?? throw new ArgumentNullException(nameof(nodeDirectory));
        this.hostClient = hostClient ?? throw new ArgumentNullException(nameof(hostClient));
        this.actorHosting = actorHosting;
        this.localNode = localNode ?? throw new ArgumentNullException(nameof(localNode));
        this.hotfixRuntime = hotfixRuntime ?? throw new ArgumentNullException(nameof(hotfixRuntime));
        this.membership = membership;
    }

    public async ValueTask<ActorPlacementResult> PlaceAsync<TActor, TKey>(
        TKey key,
        ActorPlacementCreateMode createMode,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actorType = typeof(TActor);
        var actorId = ToActorId(key);

        var existing = await actorDirectory.ResolveAsync(actorId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return new ActorPlacementResult(existing);
        }

        var selector = ResolvePlacementSelector<TActor, TKey>(actorType, actorId);
        var actorName = ActorNameResolver.Resolve(actorType);
        var now = DateTimeOffset.UtcNow;
        var records = await nodeDirectory.QueryAsync(
            new NodeDirectoryQuery(
                ClusterName,
                actorHostName: actorName,
                state: NodeState.Ready),
            now,
            cancellationToken).ConfigureAwait(false);
        var candidates = records
            .OrderBy(static record => record.NodeId.Value, StringComparer.Ordinal)
            .Select(record => new ActorHostCandidate(
                record.NodeId.Value,
                record.ActorHosts
                    .First(host => string.Equals(host.Actor, actorName, StringComparison.Ordinal))
                    .Metadata))
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
            string.Equals(record.NodeId.Value, selected.NodeId, StringComparison.Ordinal));
        if (selectedRecord is null)
        {
            throw new ActorPlacementException(
                actorType,
                actorId,
                $"Actor placement selector returned node '{selected.NodeId}', which is not one of the discovered candidates.");
        }

        var selectedHost = selectedRecord.ActorHosts.First(host =>
            string.Equals(host.Actor, actorName, StringComparison.Ordinal));
        ActorDirectoryRecord? activation = null;
        var acquiredActivation = false;
        if (membership is not null && actorDirectory is IActorActivationDirectory activationDirectory)
        {
            var snapshot = membership.Current;
            var selectedMember = snapshot.Members.FirstOrDefault(member =>
                member.Reference.Node == selectedRecord.NodeId
                && member.State == ClusterMemberState.Ready);
            if (selectedMember is null)
            {
                throw new ActorPlacementException(
                    actorType,
                    actorId,
                    $"Selected node '{selectedRecord.NodeId.Value}' is no longer a ready exact membership incarnation.");
            }

            var acquired = await activationDirectory.AcquireAsync(
                actorId,
                selectedMember.Reference,
                ActorActivationId.New(),
                cancellationToken).ConfigureAwait(false);
            activation = acquired.Record;
            acquiredActivation = acquired.Acquired;
            if (!acquired.Acquired)
            {
                return new ActorPlacementResult(acquired.Record);
            }
        }

        if (selectedRecord.NodeId == localNode.NodeId)
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
                selectedRecord.NodeId,
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
            return new ActorPlacementResult(actorId, new NodeId(reply.OwnerNode));
        }

        await ReleaseFailedActivationAsync().ConfigureAwait(false);
        throw new ActorPlacementException(
            actorType,
            actorId,
            $"Actor host create failed on node '{selectedRecord.NodeId.Value}': {reply.Message}");

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

    private static ActorId ToActorId<TKey>(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key is ActorId actorId
            ? actorId
            : ActorId.From(key.ToString() ?? throw new ArgumentException("Actor key cannot convert to an actor id.", nameof(key)));
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
