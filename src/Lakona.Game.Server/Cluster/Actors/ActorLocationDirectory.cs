using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Membership;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Actors.Internal;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;

namespace Lakona.Game.Cluster.Actors;

internal sealed class ActorLocationDirectory :
    IActorDirectory,
    IActorActivationDirectory,
    IActorLocationStabilizer,
    IActorActivationPopulationSource
{
    private const int MaximumRefreshAttempts = 2;
    private const int SnapshotPageSize = 256;
    private readonly IClusterMembership membership;
    private readonly IClusterClientFactory clients;
    private readonly LocalActorNodeIdentity localNode;
    private readonly ActorActivationRegistry registry;
    private readonly IClusterMembershipRefresher? membershipRefresher;
    private readonly ActorLocationShard?[] shards = new ActorLocationShard?[ActorLocationLayout.ShardCount];
    private readonly SemaphoreSlim[] recoveryGates = Enumerable.Range(0, ActorLocationLayout.ShardCount)
        .Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public ActorLocationDirectory(
        IClusterMembership membership,
        IClusterClientFactory clients,
        LocalActorNodeIdentity localNode,
        ActorActivationRegistry? registry = null,
        IClusterMembershipRefresher? membershipRefresher = null)
    {
        this.membership = membership;
        this.clients = clients;
        this.localNode = localNode;
        this.registry = registry ?? new ActorActivationRegistry();
        this.membershipRefresher = membershipRefresher;
    }

    ActorActivationPopulation IActorActivationPopulationSource.ObserveActivationPopulation()
    {
        var active = registry.Count;
        return new ActorActivationPopulation(active, active, 0);
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
            value.State == ClusterMemberState.Active && value.Reference.Node == node);
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
        return await ReleaseAsync(actorId, activation, cancellationToken).ConfigureAwait(false)
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
        ObserveRegistry(snapshot);
        var actorId = ActorId.From(request.ActorId);
        var shardId = ActorLocationLayout.GetShard(actorId);
        var owner = ActorLocationLayout.GetOwner(shardId, snapshot);
        if (owner is null)
        {
            ClusterDiagnostics.RecordActorLocationFailure(ActorLocationFailureReason.Unavailable);
            throw new ActorDirectoryUnavailableException("Actor Location has no Active owner.");
        }
        if (owner.Node != localNode.NodeId)
            return Reply(ActorLocationResult.Refresh(owner, snapshot.View));

        var shard = await GetShardAsync(shardId, owner, snapshot, cancellationToken).ConfigureAwait(false);
        ActorLocationResult result;
        if (method.MethodId == ActorLocationProtocol.LookupMethodId)
            result = shard.Lookup(actorId, owner, new MembershipViewId(request.View));
        else if (method.MethodId == ActorLocationProtocol.RegisterMethodId)
        {
            var activationOwner = Host(request);
            if (!snapshot.TryGetMember(activationOwner, out var hostMember)
                || hostMember?.State != ClusterMemberState.Active)
                return Reply(ActorLocationResult.Refresh(owner, snapshot.View));
            result = shard.Register(actorId, activationOwner, new ActorActivationId(request.Activation), owner, new MembershipViewId(request.View));
        }
        else if (method.MethodId == ActorLocationProtocol.UnregisterMethodId)
            result = shard.Unregister(actorId, new ActorActivationId(request.Activation), new MembershipViewId(request.View));
        else
            throw new ActorDirectoryUnavailableException($"Unknown Actor Location method id '{method.MethodId}'.");
        return Reply(result);
    }

    internal async ValueTask<ActorRegistrySnapshotReply> HandleRegistrySnapshotAsync(
        ActorRegistrySnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        var snapshot = membership.Current;
        if (request.View > snapshot.View.Value && membershipRefresher is not null)
        {
            await membershipRefresher.RefreshAsync(cancellationToken).ConfigureAwait(false);
            snapshot = membership.Current;
        }

        return HandleRegistrySnapshot(request, snapshot);
    }

    private ActorRegistrySnapshotReply HandleRegistrySnapshot(
        ActorRegistrySnapshotRequest request,
        ClusterMembershipSnapshot snapshot)
    {
        // Crossing the local Membership boundary is itself the recovery
        // watermark. Lifecycle publication orders registry Set/Remove before
        // opening or after closing mailbox admission, so observing the current
        // snapshot here is safe and avoids a startup race with the background
        // coordinator.
        ObserveRegistry(snapshot);
        if (request.View > snapshot.View.Value)
        {
            ClusterDiagnostics.RecordActorLocationFailure(ActorLocationFailureReason.Unavailable);
            throw new ActorDirectoryUnavailableException("Actor registry has not observed the requested Membership view.");
        }
        if (!registry.HasObserved(new MembershipViewId(request.View)))
        {
            ClusterDiagnostics.RecordActorLocationFailure(ActorLocationFailureReason.Unavailable);
            throw new ActorDirectoryUnavailableException("Actor activation registry has not crossed the requested recovery barrier.");
        }
        var local = snapshot.Members.SingleOrDefault(member => member.Reference.Node == localNode.NodeId);
        if (local is null || !registry.HasReachedReady)
        {
            ClusterDiagnostics.RecordActorLocationFailure(ActorLocationFailureReason.Unavailable);
            throw new ActorDirectoryUnavailableException("Actor registry is not a surviving Active-era participant.");
        }
        var records = registry.Snapshot()
            .Where(record => ActorLocationLayout.GetShard(record.ActorId) == request.Shard)
            .OrderBy(static value => value.ActorId.Value, StringComparer.Ordinal).ToArray();
        var page = records.Skip(request.Offset).Take(SnapshotPageSize).ToArray();
        return new ActorRegistrySnapshotReply
        {
            Records = page.Select(ToDto).ToArray(),
            HasMore = request.Offset + page.Length < records.Length,
            RecoveryEligible = true
        };
    }

    internal static void Bind(RpcServiceRegistry registry, ActorLocationDirectory directory)
    {
        var service = registry.RegisterSingleton(ClusterProtocol.ServiceId, directory, serviceName: "ActorLocation");
        service.Register<ActorLocationRequest, ActorLocationReply>(ActorLocationProtocol.LookupMethodId, static (d, r, ct) => d.HandleAsync(ActorLocationProtocol.Lookup, r, ct), "Lookup");
        service.Register<ActorLocationRequest, ActorLocationReply>(ActorLocationProtocol.RegisterMethodId, static (d, r, ct) => d.HandleAsync(ActorLocationProtocol.Register, r, ct), "Register");
        service.Register<ActorLocationRequest, ActorLocationReply>(ActorLocationProtocol.UnregisterMethodId, static (d, r, ct) => d.HandleAsync(ActorLocationProtocol.Unregister, r, ct), "Unregister");
        service.Register<ActorRegistrySnapshotRequest, ActorRegistrySnapshotReply>(ActorLocationProtocol.RegistrySnapshotMethodId, static (d, r, ct) => d.HandleRegistrySnapshotAsync(r, ct), "RegistrySnapshot");
    }

    public async ValueTask StabilizeAsync(
        ClusterMembershipSnapshot snapshot,
        int maximumConcurrency,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (maximumConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var outcome = "failure";
        using var activity = ClusterDiagnostics.StartActivity("cluster.actor_location.stabilize");
        try
        {
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
            outcome = "success";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "canceled";
            throw;
        }
        finally
        {
            activity?.SetTag("lakona.game.cluster.outcome", outcome);
            ClusterDiagnostics.RecordActorLocationRecovery(
                outcome,
                System.Diagnostics.Stopwatch.GetElapsedTime(started));
        }
    }

    public void ObserveRecoveryView(ClusterMembershipSnapshot snapshot) => ObserveRegistry(snapshot);

    private void ObserveRegistry(ClusterMembershipSnapshot snapshot)
    {
        registry.Observe(
            snapshot.View,
            snapshot.Members.Any(member =>
                member.Reference.Node == localNode.NodeId
                && member.State == ClusterMemberState.Active));
    }

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
            var owner = ActorLocationLayout.GetOwner(
                ActorLocationLayout.GetShard(ActorId.From(request.ActorId)),
                snapshot);
            if (owner is null)
            {
                ClusterDiagnostics.RecordActorLocationFailure(ActorLocationFailureReason.Unavailable);
                throw new ActorDirectoryUnavailableException("Actor Location has no Active owner.");
            }
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
            {
                ClusterDiagnostics.RecordActorLocationFailure(ActorLocationFailureReason.Capacity);
                throw new ActorDirectoryUnavailableException("Actor Location shard capacity is exhausted.");
            }
            if (result.Status != ActorLocationMutationStatus.RefreshRequired) return result;
        }
        ClusterDiagnostics.RecordActorLocationFailure(ActorLocationFailureReason.Unavailable);
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
            var recovered = await RecoverAsync(id, snapshot, cancellationToken).ConfigureAwait(false);
            var created = new ActorLocationShard(owner, snapshot.View);
            created.Restore(recovered);
            var current = membership.Current;
            if (ActorLocationLayout.GetOwner(id, current) != owner
                || current.View.Value > snapshot.View.Value + 1)
            {
                ClusterDiagnostics.RecordActorLocationFailure(ActorLocationFailureReason.Unavailable);
                throw new ActorDirectoryUnavailableException("Actor Location ownership changed during recovery.");
            }
            created.AdvanceRecoveredOwner(owner, current.View);
            Volatile.Write(ref shards[id], created);
            return created;
        }
        finally { recoveryGates[id].Release(); }
    }

    private async ValueTask<IReadOnlyList<ActorDirectoryRecord>> RecoverAsync(
        int shard,
        ClusterMembershipSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var recovered = new Dictionary<ActorId, ActorDirectoryRecord>();
        foreach (var member in snapshot.Members.Where(static value =>
                     value.State == ClusterMemberState.Active))
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
                {
                    ClusterDiagnostics.RecordActorLocationFailure(ActorLocationFailureReason.Conflict);
                    throw new ActorDirectoryUnavailableException($"Conflicting live activations were recovered for '{record.ActorId.Value}'.");
                }
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
            var reply = HandleRegistrySnapshot(SnapshotRequest(shard, snapshot, offset), membership.Current);
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
            {
                ClusterDiagnostics.RecordActorLocationFailure(ActorLocationFailureReason.Unavailable);
                throw new ActorDirectoryUnavailableException("Actor Location recovery participant was not eligible.");
            }
            result.AddRange(reply.Records.Select(FromDto));
            if (result.Count > ActorLocationShard.MaximumRecords)
            {
                ClusterDiagnostics.RecordActorLocationFailure(ActorLocationFailureReason.Capacity);
                throw new ActorDirectoryUnavailableException("Actor Location shard capacity is exhausted during recovery.");
            }
            if (!reply.HasMore) return result;
            if (reply.Records.Count == 0)
            {
                ClusterDiagnostics.RecordActorLocationFailure(ActorLocationFailureReason.Unavailable);
                throw new ActorDirectoryUnavailableException("Actor Location snapshot pagination made no progress.");
            }
            offset += reply.Records.Count;
        }
    }

    private ActorRegistrySnapshotRequest SnapshotRequest(int shard, ClusterMembershipSnapshot snapshot, int offset)
    {
        return new ActorRegistrySnapshotRequest
        {
            Shard = shard,
            View = snapshot.View.Value,
            Offset = offset
        };
    }

    private ActorLocationRequest Request(ActorId actorId) => new() { ActorId = actorId.Value };
    private static NodeReference Host(ActorLocationRequest r) => new(new ClusterIncarnationId(r.HostCluster), new NodeId(r.HostNode), new NodeIncarnationId(r.HostIncarnation));
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
            new ActorActivationId(r.Activation), DateTimeOffset.UtcNow);
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
        new ActorActivationId(r.Activation), DateTimeOffset.UtcNow);
}
