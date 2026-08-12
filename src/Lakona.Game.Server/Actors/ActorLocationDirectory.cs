using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Actors;

internal sealed class ActorLocationDirectory : IActorDirectory, IActorActivationDirectory
{
    private const int MaximumRefreshAttempts = 2;
    private readonly IClusterMembership membership;
    private readonly IClusterClientFactory clients;
    private readonly LocalActorNodeIdentity localNode;
    private readonly ActorActivationRegistry registry;
    private readonly ActorLocationShard?[] shards = new ActorLocationShard?[ActorLocationLayout.ShardCount];
    private readonly SemaphoreSlim[] recoveryGates = Enumerable.Range(0, ActorLocationLayout.ShardCount)
        .Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public ActorLocationDirectory(
        IClusterMembership membership,
        IClusterClientFactory clients,
        LocalActorNodeIdentity localNode,
        ActorActivationRegistry? registry = null)
    {
        this.membership = membership;
        this.clients = clients;
        this.localNode = localNode;
        this.registry = registry ?? new ActorActivationRegistry();
    }

    public async ValueTask<ActorDirectoryRecord?> ResolveAsync(
        ActorId actorId,
        CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(ActorLocationProtocol.Lookup, Request(actorId), cancellationToken)
            .ConfigureAwait(false)).Record;

    public async ValueTask<ActorDirectoryRegisterStatus> RegisterAsync(
        ActorId actorId,
        NodeId node,
        CancellationToken cancellationToken = default)
    {
        var member = membership.Current.Members.SingleOrDefault(value =>
            value.State == ClusterMemberState.Ready && value.Reference.Node == node);
        if (member is null) return ActorDirectoryRegisterStatus.Conflict;
        var result = await AcquireAsync(actorId, member.Reference, ActorActivationId.New(), cancellationToken)
            .ConfigureAwait(false);
        return result.Acquired ? ActorDirectoryRegisterStatus.Registered : ActorDirectoryRegisterStatus.AlreadyRegistered;
    }

    public async ValueTask<ActorDirectoryUnregisterStatus> UnregisterAsync(
        ActorId actorId,
        NodeId node,
        CancellationToken cancellationToken = default)
    {
        var existing = await ResolveAsync(actorId, cancellationToken).ConfigureAwait(false);
        if (existing is null) return ActorDirectoryUnregisterStatus.NotFound;
        if (existing.Node != node || existing.ActivationId is not { } activation)
            return ActorDirectoryUnregisterStatus.OwnershipMismatch;
        return await ReleaseAsync(actorId, activation, existing.Version, cancellationToken).ConfigureAwait(false)
            ? ActorDirectoryUnregisterStatus.Unregistered
            : ActorDirectoryUnregisterStatus.OwnershipMismatch;
    }

    public async ValueTask<ActorActivationAcquireResult> AcquireAsync(
        ActorId actorId,
        NodeReference proposedOwner,
        ActorActivationId proposedActivation,
        CancellationToken cancellationToken = default)
    {
        var request = Request(actorId);
        request.HostCluster = proposedOwner.Cluster.Value;
        request.HostNode = proposedOwner.Node.Value;
        request.HostIncarnation = proposedOwner.Incarnation.Value;
        request.Activation = proposedActivation.Value;
        var result = await ExecuteAsync(ActorLocationProtocol.Register, request, cancellationToken)
            .ConfigureAwait(false);
        return new ActorActivationAcquireResult(
            result.Record ?? throw new ActorDirectoryUnavailableException("Actor registration returned no record."),
            result.Status == ActorLocationMutationStatus.Applied);
    }

    public async ValueTask<bool> ReleaseAsync(
        ActorId actorId,
        ActorActivationId expectedActivation,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var request = Request(actorId);
        request.Activation = expectedActivation.Value;
        var result = await ExecuteAsync(ActorLocationProtocol.Unregister, request, cancellationToken)
            .ConfigureAwait(false);
        return result.Status == ActorLocationMutationStatus.Applied;
    }

    internal async ValueTask<ActorLocationReply> HandleAsync(
        RpcMethod<ActorLocationRequest, ActorLocationReply> method,
        ActorLocationRequest request,
        CancellationToken cancellationToken)
    {
        var snapshot = membership.Current;
        registry.Observe(snapshot, localNode.NodeId);
        var actorId = ActorId.From(request.ActorId);
        var shardId = ActorLocationLayout.GetShard(actorId);
        var owner = ActorLocationLayout.GetOwner(shardId, snapshot)
            ?? throw new ActorDirectoryUnavailableException("Actor Location has no Ready owner.");
        if (owner.Node != localNode.NodeId)
            return Reply(ActorLocationResult.Refresh(owner, snapshot.View));

        var shard = await GetShardAsync(shardId, owner, snapshot, cancellationToken).ConfigureAwait(false);
        var callerOwner = ActorLocationLayout.GetOwner(shardId, snapshot)!;
        ActorLocationResult result;
        if (method.MethodId == ActorLocationProtocol.LookupMethodId)
            result = shard.Lookup(actorId, callerOwner, new MembershipViewId(request.View));
        else if (method.MethodId == ActorLocationProtocol.RegisterMethodId)
        {
            var activationOwner = Host(request);
            if (!snapshot.TryGetMember(activationOwner, out var hostMember)
                || hostMember?.State != ClusterMemberState.Ready)
                return Reply(ActorLocationResult.Refresh(callerOwner, snapshot.View));
            result = shard.Register(actorId, activationOwner, new ActorActivationId(request.Activation), callerOwner, new MembershipViewId(request.View));
        }
        else
            result = shard.Unregister(actorId, new ActorActivationId(request.Activation), new MembershipViewId(request.View));
        return Reply(result);
    }

    internal ActorRegistrySnapshotReply HandleRegistrySnapshot(ActorRegistrySnapshotRequest request)
    {
        var snapshot = membership.Current;
        // Crossing the local Membership boundary is itself the recovery
        // watermark. Lifecycle publication orders registry Set/Remove before
        // opening or after closing mailbox admission, so observing the current
        // snapshot here is safe and avoids a startup race with the background
        // coordinator.
        registry.Observe(snapshot, localNode.NodeId);
        if (request.View > snapshot.View.Value)
            throw new ActorDirectoryUnavailableException("Actor registry has not observed the requested Membership view.");
        if (!registry.HasObserved(new MembershipViewId(request.View)))
            throw new ActorDirectoryUnavailableException("Actor activation registry has not crossed the requested recovery barrier.");
        var local = snapshot.Members.SingleOrDefault(member => member.Reference.Node == localNode.NodeId);
        if (local is null || !registry.HasReachedReady)
            throw new ActorDirectoryUnavailableException("Actor registry is not a surviving Ready-era participant.");
        var records = registry.SnapshotShard(request.Shard)
            .OrderBy(static value => value.ActorId.Value, StringComparer.Ordinal).ToArray();
        var page = records.Skip(request.Offset).Take(ActorLocationShard.SnapshotPageSize).ToArray();
        return new ActorRegistrySnapshotReply
        {
            Records = page.Select(ToDto).ToArray(),
            HasMore = request.Offset + page.Length < records.Length,
            RecoveryEligible = true
        };
    }

    internal ActorRegistrySnapshotReply HandleShardSnapshot(ActorRegistrySnapshotRequest request)
    {
        var snapshot = membership.Current;
        var owner = ActorLocationLayout.GetOwner(request.Shard, snapshot);
        var requester = Requester(request);
        if (owner != requester)
            throw new ActorDirectoryUnavailableException("Actor Location shard handoff requester is not the current exact owner.");
        if (owner?.Node == localNode.NodeId)
            throw new ActorDirectoryUnavailableException("The current Actor Location owner cannot seal its serving shard.");
        var shard = Volatile.Read(ref shards[request.Shard]);
        if (shard is null)
            return new ActorRegistrySnapshotReply();
        shard.SealAndSnapshot(new MembershipViewId(request.View));
        var page = shard.SnapshotPage(request.Offset);
        return new ActorRegistrySnapshotReply
        {
            Records = page.Records.Select(ToDto).ToArray(),
            HasMore = page.HasMore,
            RecoveryEligible = true
        };
    }

    internal static void Bind(RpcServiceRegistry registry, ActorLocationDirectory directory)
    {
        var service = registry.RegisterSingleton(ClusterProtocol.ServiceId, directory, serviceName: "ActorLocation");
        service.Register<ActorLocationRequest, ActorLocationReply>(ActorLocationProtocol.LookupMethodId, static (d, r, ct) => d.HandleAsync(ActorLocationProtocol.Lookup, r, ct), "Lookup");
        service.Register<ActorLocationRequest, ActorLocationReply>(ActorLocationProtocol.RegisterMethodId, static (d, r, ct) => d.HandleAsync(ActorLocationProtocol.Register, r, ct), "Register");
        service.Register<ActorLocationRequest, ActorLocationReply>(ActorLocationProtocol.UnregisterMethodId, static (d, r, ct) => d.HandleAsync(ActorLocationProtocol.Unregister, r, ct), "Unregister");
        service.Register<ActorRegistrySnapshotRequest, ActorRegistrySnapshotReply>(ActorLocationProtocol.RegistrySnapshotMethodId, static (d, r, _) => new ValueTask<ActorRegistrySnapshotReply>(d.HandleRegistrySnapshot(r)), "RegistrySnapshot");
        service.Register<ActorRegistrySnapshotRequest, ActorRegistrySnapshotReply>(ActorLocationProtocol.ShardSnapshotMethodId, static (d, r, _) => new ValueTask<ActorRegistrySnapshotReply>(d.HandleShardSnapshot(r)), "ShardSnapshot");
    }

    internal async ValueTask StabilizeAsync(
        ClusterMembershipSnapshot snapshot,
        int maximumConcurrency,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (maximumConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        using var concurrency = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
        var work = new List<Task>();
        for (var shard = 0; shard < ActorLocationLayout.ShardCount; shard++)
        {
            var owner = ActorLocationLayout.GetOwner(shard, snapshot);
            if (owner?.Node != localNode.NodeId) continue;
            await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            var capturedShard = shard;
            work.Add(StabilizeShardAsync(capturedShard, owner, snapshot, concurrency, cancellationToken));
        }
        await Task.WhenAll(work).ConfigureAwait(false);
    }

    internal void ObserveRecoveryView(ClusterMembershipSnapshot snapshot) => registry.Observe(snapshot, localNode.NodeId);

    private async Task StabilizeShardAsync(
        int shard,
        NodeReference owner,
        ClusterMembershipSnapshot snapshot,
        SemaphoreSlim concurrency,
        CancellationToken cancellationToken)
    {
        try
        {
            await GetShardAsync(shard, owner, snapshot, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            concurrency.Release();
        }
    }

    private async ValueTask<ActorLocationResult> ExecuteAsync(
        RpcMethod<ActorLocationRequest, ActorLocationReply> method,
        ActorLocationRequest request,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumRefreshAttempts; attempt++)
        {
            var snapshot = membership.Current;
            request.View = snapshot.View.Value;
            var owner = ActorLocationLayout.GetOwner(ActorLocationLayout.GetShard(ActorId.From(request.ActorId)), snapshot)
                ?? throw new ActorDirectoryUnavailableException("Actor Location has no Ready owner.");
            ActorLocationReply reply;
            if (owner.Node == localNode.NodeId) reply = await HandleAsync(method, request, cancellationToken).ConfigureAwait(false);
            else
            {
                var member = snapshot.Members.Single(value => value.Reference == owner);
                var target = new RouteLocation(new RouteKey("actor-location"), owner, snapshot.View, member.ClusterEndpoint);
                var client = await clients.GetClientAsync(target, cancellationToken).ConfigureAwait(false);
                reply = await client.CallAsync(method, request, cancellationToken).ConfigureAwait(false);
            }
            var result = Result(reply, ActorId.From(request.ActorId));
            if (result.Status == ActorLocationMutationStatus.Unavailable)
                throw new ActorDirectoryUnavailableException("Actor Location shard is changing owner.");
            if (result.Status != ActorLocationMutationStatus.RefreshRequired) return result;
        }
        throw new ActorDirectoryUnavailableException("Actor Location could not converge on one shard owner.");
    }

    private async ValueTask<ActorLocationShard> GetShardAsync(
        int id,
        NodeReference owner,
        ClusterMembershipSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var existing = Volatile.Read(ref shards[id]);
        if (existing is not null && existing.TryAdvanceStableOwner(owner, snapshot.View)) return existing;
        await recoveryGates[id].WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            existing = Volatile.Read(ref shards[id]);
            if (existing is not null && existing.TryAdvanceStableOwner(owner, snapshot.View)) return existing;
            var recovered = existing is not null && existing.Owner != owner
                ? await TransferOrRecoverAsync(id, existing, snapshot, cancellationToken).ConfigureAwait(false)
                : await RecoverAsync(id, snapshot, cancellationToken).ConfigureAwait(false);
            var created = new ActorLocationShard(owner, snapshot.View);
            created.Restore(recovered);
            var current = membership.Current;
            if (ActorLocationLayout.GetOwner(id, current) != owner
                || current.View.Value > snapshot.View.Value + 1)
                throw new ActorDirectoryUnavailableException("Actor Location ownership changed during recovery.");
            created.AdvanceRecoveredOwner(owner, current.View);
            Volatile.Write(ref shards[id], created);
            return created;
        }
        finally { recoveryGates[id].Release(); }
    }

    private async ValueTask<IReadOnlyList<ActorDirectoryRecord>> TransferOrRecoverAsync(
        int shard,
        ActorLocationShard previous,
        ClusterMembershipSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.TryGetMember(previous.Owner, out var oldMember)
            && oldMember is { State: ClusterMemberState.Ready })
        {
            if (oldMember.Reference.Node == localNode.NodeId)
                return previous.SealAndSnapshot(snapshot.View);
            var target = new RouteLocation(new RouteKey("actor-location-shard"), oldMember.Reference, snapshot.View, oldMember.ClusterEndpoint);
            var client = await clients.GetClientAsync(target, cancellationToken).ConfigureAwait(false);
            return await ReadAllPagesAsync(
                client,
                ActorLocationProtocol.ShardSnapshot,
                shard,
                snapshot,
                cancellationToken).ConfigureAwait(false);
        }
        return await RecoverAsync(shard, snapshot, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyList<ActorDirectoryRecord>> RecoverAsync(
        int shard,
        ClusterMembershipSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var recovered = new Dictionary<ActorId, ActorDirectoryRecord>();
        foreach (var member in snapshot.Members.Where(static value =>
                     value.State is not ClusterMemberState.Joining and not ClusterMemberState.Recovering))
        {
            IReadOnlyList<ActorDirectoryRecord> records;
            if (member.Reference.Node == localNode.NodeId)
            {
                records = ReadLocalRegistryPages(shard, snapshot);
            }
            else
            {
                var target = new RouteLocation(new RouteKey("actor-registry"), member.Reference, snapshot.View, member.ClusterEndpoint);
                var client = await clients.GetClientAsync(target, cancellationToken).ConfigureAwait(false);
                records = await ReadAllPagesAsync(client, ActorLocationProtocol.RegistrySnapshot, shard, snapshot, cancellationToken)
                    .ConfigureAwait(false);
            }

            foreach (var record in records)
            {
                if (recovered.TryGetValue(record.ActorId, out var conflict)
                    && (conflict.OwnerReference != record.OwnerReference || conflict.ActivationId != record.ActivationId))
                    throw new ActorDirectoryUnavailableException($"Conflicting live activations were recovered for '{record.ActorId.Value}'.");
                recovered[record.ActorId] = record;
            }
        }
        return recovered.Values.ToArray();
    }

    private IReadOnlyList<ActorDirectoryRecord> ReadLocalRegistryPages(int shard, ClusterMembershipSnapshot snapshot)
    {
        var result = new List<ActorDirectoryRecord>();
        var offset = 0;
        while (true)
        {
            var reply = HandleRegistrySnapshot(SnapshotRequest(shard, snapshot, offset));
            result.AddRange(reply.Records.Select(FromDto));
            if (!reply.HasMore) return result;
            offset += reply.Records.Count;
        }
    }

    private async ValueTask<IReadOnlyList<ActorDirectoryRecord>> ReadAllPagesAsync(
        IRpcClient client,
        RpcMethod<ActorRegistrySnapshotRequest, ActorRegistrySnapshotReply> method,
        int shard,
        ClusterMembershipSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var result = new List<ActorDirectoryRecord>();
        var offset = 0;
        while (true)
        {
            var reply = await client.CallAsync(method, SnapshotRequest(shard, snapshot, offset), cancellationToken)
                .ConfigureAwait(false);
            if (!reply.RecoveryEligible)
                throw new ActorDirectoryUnavailableException("Actor Location recovery participant was not eligible.");
            result.AddRange(reply.Records.Select(FromDto));
            if (result.Count > ActorLocationShard.MaximumRecords)
                throw new ActorDirectoryUnavailableException("Actor Location shard capacity is exhausted during recovery.");
            if (!reply.HasMore) return result;
            if (reply.Records.Count == 0)
                throw new ActorDirectoryUnavailableException("Actor Location snapshot pagination made no progress.");
            offset += reply.Records.Count;
        }
    }

    private ActorRegistrySnapshotRequest SnapshotRequest(int shard, ClusterMembershipSnapshot snapshot, int offset)
    {
        var requester = ActorLocationLayout.GetOwner(shard, snapshot)
            ?? throw new ActorDirectoryUnavailableException("Actor Location has no Ready owner.");
        return new ActorRegistrySnapshotRequest
        {
            Shard = shard,
            View = snapshot.View.Value,
            Offset = offset,
            RequesterCluster = requester.Cluster.Value,
            RequesterNode = requester.Node.Value,
            RequesterIncarnation = requester.Incarnation.Value
        };
    }

    private ActorLocationRequest Request(ActorId actorId) => new() { ActorId = actorId.Value };
    private static NodeReference Host(ActorLocationRequest r) => new(new ClusterIncarnationId(r.HostCluster), new NodeId(r.HostNode), new NodeIncarnationId(r.HostIncarnation));
    private static NodeReference Requester(ActorRegistrySnapshotRequest r) => new(new ClusterIncarnationId(r.RequesterCluster), new NodeId(r.RequesterNode), new NodeIncarnationId(r.RequesterIncarnation));
    private static ActorLocationReply Reply(ActorLocationResult r) => new()
    {
        Status = (int)r.Status, View = r.View.Value,
        OwnerCluster = r.Owner.Cluster.Value, OwnerNode = r.Owner.Node.Value, OwnerIncarnation = r.Owner.Incarnation.Value,
        HostCluster = r.Record?.OwnerReference?.Cluster.Value ?? Guid.Empty,
        HostNode = r.Record?.OwnerReference?.Node.Value ?? string.Empty,
        HostIncarnation = r.Record?.OwnerReference?.Incarnation.Value ?? Guid.Empty,
        Activation = r.Record?.ActivationId?.Value ?? Guid.Empty
    };
    private static ActorLocationResult Result(ActorLocationReply r, ActorId actorId)
    {
        var owner = new NodeReference(new ClusterIncarnationId(r.OwnerCluster), new NodeId(r.OwnerNode), new NodeIncarnationId(r.OwnerIncarnation));
        ActorDirectoryRecord? record = r.Activation == Guid.Empty ? null : new ActorDirectoryRecord(actorId,
            new NodeReference(new ClusterIncarnationId(r.HostCluster), new NodeId(r.HostNode), new NodeIncarnationId(r.HostIncarnation)),
            new ActorActivationId(r.Activation), 1, DateTimeOffset.UtcNow);
        return new ActorLocationResult((ActorLocationMutationStatus)r.Status, record, owner, new MembershipViewId(r.View));
    }
    private static ActorLocationRecordDto ToDto(ActorDirectoryRecord r) => new()
    {
        ActorId = r.ActorId.Value,
        HostCluster = r.OwnerReference!.Cluster.Value,
        HostNode = r.OwnerReference.Node.Value,
        HostIncarnation = r.OwnerReference.Incarnation.Value,
        Activation = r.ActivationId!.Value.Value
    };
    private static ActorDirectoryRecord FromDto(ActorLocationRecordDto r) => new(
        ActorId.From(r.ActorId),
        new NodeReference(new ClusterIncarnationId(r.HostCluster), new NodeId(r.HostNode), new NodeIncarnationId(r.HostIncarnation)),
        new ActorActivationId(r.Activation), 1, DateTimeOffset.UtcNow);
}
