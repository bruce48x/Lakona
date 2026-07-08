using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Actors;

public sealed class ActorPlacementService(
    IActorDirectory actorDirectory,
    INodeDirectory nodeDirectory,
    IActorHostClient hostClient,
    IHotfixRuntimeAccessor hotfixRuntime) : IActorPlacementService
{
    private const string ClusterName = "local";

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
            return new ActorPlacementResult(actorId, existing.Node);
        }

        var placement = ResolvePlacement<TActor, TKey>(actorType, actorId);
        var actorName = ResolveActorName(actorType);
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
            var selector = (Func<ActorPlacementContext<TKey>, ActorHostCandidate>)placement.Selector;
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
        var reply = await hostClient.CreateAsync(
            selectedRecord.NodeId,
            new ActorHostCreateRequest(
                actorName,
                actorId.Value,
                ToWireMode(createMode),
                selectedHost.BuildTag),
            cancellationToken).ConfigureAwait(false);
        if (reply.Succeeded && !string.IsNullOrWhiteSpace(reply.OwnerNode))
        {
            return new ActorPlacementResult(actorId, new NodeId(reply.OwnerNode));
        }

        if (!reply.Succeeded && !string.IsNullOrWhiteSpace(reply.OwnerNode))
        {
            return new ActorPlacementResult(actorId, new NodeId(reply.OwnerNode));
        }

        throw new ActorPlacementException(
            actorType,
            actorId,
            $"Actor host create failed on node '{selectedRecord.NodeId.Value}': {reply.Message}");
    }

    private ActorPlacementDeclaration ResolvePlacement<TActor, TKey>(
        Type actorType,
        ActorId actorId)
        where TActor : class, IActor
    {
        using var lease = hotfixRuntime.AcquireCurrent();
        var placement = lease.Snapshot.ActorPlacements
            .FirstOrDefault(item => item.ActorType == actorType);
        if (placement is null)
        {
            throw new ActorPlacementException(
                actorType,
                actorId,
                $"No actor placement selector is registered for '{actorType.FullName}'.");
        }

        if (placement.KeyType != typeof(TKey))
        {
            throw new ActorPlacementException(
                actorType,
                actorId,
                $"Actor placement selector for '{actorType.FullName}' expects key type '{placement.KeyType.FullName}', not '{typeof(TKey).FullName}'.");
        }

        return placement;
    }

    private static ActorId ToActorId<TKey>(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key is ActorId actorId
            ? actorId
            : ActorId.From(key.ToString() ?? throw new ArgumentException("Actor key cannot convert to an actor id.", nameof(key)));
    }

    private static string ResolveActorName(Type actorType)
    {
        var attribute = (ActorNameAttribute?)Attribute.GetCustomAttribute(actorType, typeof(ActorNameAttribute), inherit: false);
        return attribute?.Name ?? actorType.Name;
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
