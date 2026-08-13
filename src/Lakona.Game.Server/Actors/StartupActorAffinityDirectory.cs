using System.Collections.Concurrent;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using MemoryPack;

namespace Lakona.Game.Server.Actors;

internal sealed record StartupActorAffinityRecord(ActorId AffinityId, NodeReference Target, long Generation, bool Pending = false);

internal interface IStartupActorAffinityDirectory
{
    ValueTask<StartupActorAffinityRecord?> LookupAsync(ActorId id, CancellationToken cancellationToken);
    ValueTask<StartupActorAffinityRecord> BindAsync(
        ActorId id, NodeReference target, string actorName, string policyHash, string buildTag,
        CancellationToken cancellationToken);
}

internal sealed class StartupActorAffinityDirectory : IStartupActorAffinityDirectory
{
    internal const int MaximumRowsPerShard = 4096;
    private const int LookupId = 32, BindId = 33, CatalogLookupId = 35, RetainId = 36, OwnerSnapshotId = 37;
    private static readonly RpcMethod<AffinityRequest, AffinityReply> LookupRpc = new(ClusterProtocol.ServiceId, LookupId);
    private static readonly RpcMethod<AffinityRequest, AffinityReply> BindRpc = new(ClusterProtocol.ServiceId, BindId);
    private static readonly RpcMethod<AffinityRequest, AffinityReply> CatalogLookupRpc = new(ClusterProtocol.ServiceId, CatalogLookupId);
    private static readonly RpcMethod<AffinityRequest, AffinityReply> RetainRpc = new(ClusterProtocol.ServiceId, RetainId);
    private static readonly RpcMethod<AffinityRequest, AffinityReply> OwnerSnapshotRpc = new(ClusterProtocol.ServiceId, OwnerSnapshotId);
    private readonly AffinityShard[] shards =
        Enumerable.Range(0, ActorLocationLayout.ShardCount).Select(_ => new AffinityShard()).ToArray();
    private readonly AffinityShard[] catalog =
        Enumerable.Range(0, ActorLocationLayout.ShardCount).Select(_ => new AffinityShard()).ToArray();
    private readonly SemaphoreSlim[] recoveryGates = Enumerable.Range(0, ActorLocationLayout.ShardCount)
        .Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly IClusterMembership? membership;
    private readonly IClusterClientFactory? clients;
    private readonly LocalActorNodeIdentity? localNode;

    public StartupActorAffinityDirectory() { }
    public StartupActorAffinityDirectory(IClusterMembership membership, IClusterClientFactory clients, LocalActorNodeIdentity localNode)
        => (this.membership, this.clients, this.localNode) = (membership, clients, localNode);

    public async ValueTask<StartupActorAffinityRecord?> LookupAsync(ActorId id, CancellationToken cancellationToken)
        => membership is null ? LocalLookup(id) : FromReply(id, await RouteAsync(LookupRpc, Request(id), cancellationToken).ConfigureAwait(false));

    public async ValueTask<StartupActorAffinityRecord> BindAsync(
        ActorId id, NodeReference target, string actorName, string policyHash, string buildTag,
        CancellationToken cancellationToken)
    {
        if (membership is null) return LocalBind(id, target);
        var request = Request(id, target);
        request.ActorName = actorName;
        request.PolicyHash = policyHash;
        request.BuildTag = buildTag;
        return FromReply(id, await RouteAsync(BindRpc, request, cancellationToken).ConfigureAwait(false))
            ?? throw new ActorDirectoryUnavailableException("Startup affinity bind returned no target.");
    }

    private async ValueTask<AffinityReply> RouteAsync(RpcMethod<AffinityRequest, AffinityReply> method, AffinityRequest request, CancellationToken ct)
    {
        var snapshot = membership!.Current;
        var id = ActorId.From(request.Id);
        var owner = ActorLocationLayout.GetOwner(ActorLocationLayout.GetShard(id), snapshot)
            ?? throw new StartupActorUnavailableException(typeof(IActor));
        StampAuthority(request, owner, snapshot.View);
        if (owner.Node == localNode!.NodeId) return await HandleAsync(method, request, ct).ConfigureAwait(false);
        var member = snapshot.Members.Single(value => value.Reference == owner);
        var client = await clients!.GetClientAsync(new RouteLocation(new RouteKey("startup-affinity"), owner, snapshot.View, member.ClusterEndpoint), ct).ConfigureAwait(false);
        return await client.CallAsync(method, request, ct).ConfigureAwait(false);
    }

    private async ValueTask<AffinityReply> HandleAsync(RpcMethod<AffinityRequest, AffinityReply> method, AffinityRequest request, CancellationToken ct)
    {
        var snapshot = membership!.Current;
        var id = ActorId.From(request.Id);
        var shardId = request.Shard >= 0 ? request.Shard : ActorLocationLayout.GetShard(id);
        if ((uint)shardId >= ActorLocationLayout.ShardCount)
            throw new ActorDirectoryUnavailableException("Startup affinity shard is invalid.");
        var owner = ActorLocationLayout.GetOwner(shardId, snapshot);
        var authority = Authority(request);
        if (owner != authority) throw new ActorDirectoryUnavailableException("Startup affinity authority stamp is stale.");
        if (request.View > snapshot.View.Value)
            throw new ActorDirectoryUnavailableException("Startup affinity participant has not reached the requested Membership view.");
        if (method.MethodId == OwnerSnapshotId)
            return Reply(shards[shardId].HandoffSnapshot(authority, new MembershipViewId(request.View)));
        if (method.MethodId is CatalogLookupId or RetainId)
        {
            if (method.MethodId == RetainId && Target(request) != snapshot.Members
                    .SingleOrDefault(member => member.Reference.Node == localNode!.NodeId)?.Reference)
                throw new ActorDirectoryUnavailableException("Startup affinity catalog retain reached the wrong exact replica.");
            var replica = catalog[shardId];
            if (method.MethodId == CatalogLookupId)
            {
                if (request.Shard >= 0)
                    return Reply(replica.FenceAndSnapshot(authority, new MembershipViewId(request.View)));
                return Reply(replica.FenceAndLookup(authority, new MembershipViewId(request.View), id), false);
            }
            var retained = replica.FencedBind(authority, new MembershipViewId(request.View), id, Target(request), Math.Max(1, request.Generation));
            return Reply(retained, true);
        }
        if (owner?.Node != localNode!.NodeId) return new AffinityReply();
        await EnsureOwnerShardAsync(shardId, owner, snapshot, ct).ConfigureAwait(false);
        if (method.MethodId == LookupId)
        {
            return Reply(LocalLookup(id), false);
        }
        var target = Target(request);
        var existing = LocalLookup(id);
        if (existing?.Pending == true)
        {
            if (snapshot.TryGetMember(existing.Target, out var pendingMember)
                && pendingMember?.State == ClusterMemberState.Ready)
            {
                await RetainAsync(id, existing.Target, existing.Generation, snapshot, ct).ConfigureAwait(false);
                return Reply(LocalBind(id, existing.Target, existing.Generation, pending: false), false);
            }
        }
        if (existing is not null && snapshot.TryGetMember(existing.Target, out var current)
            && current?.State == ClusterMemberState.Ready
            && current.StartupActors.Any(value =>
                string.Equals(value.Actor, request.ActorName, StringComparison.Ordinal)
                && string.Equals(value.PolicyHash, request.PolicyHash, StringComparison.Ordinal)
                && string.Equals(value.BuildTag, request.BuildTag, StringComparison.Ordinal)))
        {
            if (existing.Pending)
            {
                await RetainAsync(id, existing.Target, existing.Generation, snapshot, ct).ConfigureAwait(false);
                existing = LocalBind(id, existing.Target, existing.Generation, pending: false);
            }
            return Reply(existing, false);
        }
        if (!snapshot.TryGetMember(target, out var member) || member?.State != ClusterMemberState.Ready
            || !member.StartupActors.Any(value =>
                string.Equals(value.Actor, request.ActorName, StringComparison.Ordinal)
                && string.Equals(value.PolicyHash, request.PolicyHash, StringComparison.Ordinal)
                && string.Equals(value.BuildTag, request.BuildTag, StringComparison.Ordinal)))
            throw new StartupActorUnavailableException(typeof(IActor));
        var pending = existing?.Pending == true
            ? shards[shardId].ReplacePendingTarget(id, existing.Target, target)
            : LocalBind(id, target, (existing?.Generation ?? 0) + 1, pending: true);
        await RetainAsync(id, target, pending.Generation, snapshot, ct).ConfigureAwait(false);
        return Reply(LocalBind(id, target, pending.Generation, pending: false), true);
    }

    private async ValueTask EnsureOwnerShardAsync(
        int shardId,
        NodeReference owner,
        ClusterMembershipSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var shard = shards[shardId];
        if (shard.TryAdvance(owner, snapshot.View)) return;
        await recoveryGates[shardId].WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (shard.TryAdvance(owner, snapshot.View)) return;
            var recovered = new List<StartupActorAffinityRecord>();
            // Startup affinity volume is intentionally small. Recovering the
            // complete shard makes the capacity bound and generation lineage
            // part of the handoff instead of reconstructing one key at a time.
            foreach (var member in snapshot.Members.Where(static value => value.State == ClusterMemberState.Ready))
            {
                var ownerRows = await ReadOwnerShardAsync(shardId, member, owner, snapshot, cancellationToken)
                    .ConfigureAwait(false);
                var rows = await ReadCatalogShardAsync(shardId, member, owner, snapshot, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var row in ownerRows.Concat(rows))
                {
                    var existing = recovered.FirstOrDefault(value => value.AffinityId == row.AffinityId);
                    if (existing is not null && existing.Generation == row.Generation && existing.Target != row.Target)
                        throw new ActorDirectoryUnavailableException($"Conflicting Startup affinity generation for '{row.AffinityId.Value}'.");
                    if (existing is null || row.Generation > existing.Generation)
                    {
                        if (existing is not null) recovered.Remove(existing);
                        recovered.Add(row);
                    }
                }
            }
            var current = membership!.Current;
            if (ActorLocationLayout.GetOwner(shardId, current) != owner || current.View.Value > snapshot.View.Value + 1)
                throw new ActorDirectoryUnavailableException("Startup affinity ownership changed during recovery.");
            shard.Activate(owner, current.View, recovered);
        }
        finally { recoveryGates[shardId].Release(); }
    }

    private async ValueTask<IReadOnlyList<StartupActorAffinityRecord>> ReadOwnerShardAsync(
        int shardId,
        ClusterMember member,
        NodeReference authority,
        ClusterMembershipSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (member.Reference == authority) return [];
        if (member.Reference.Node == localNode!.NodeId)
            return shards[shardId].HandoffSnapshot(authority, snapshot.View);
        var client = await clients!.GetClientAsync(
            new RouteLocation(new RouteKey("startup-affinity-owner"), member.Reference, snapshot.View, member.ClusterEndpoint),
            cancellationToken).ConfigureAwait(false);
        var request = Request(ActorId.From($"@startup-affinity-shard/{shardId}"));
        request.Shard = shardId;
        StampAuthority(request, authority, snapshot.View);
        var reply = await client.CallAsync(OwnerSnapshotRpc, request, cancellationToken).ConfigureAwait(false);
        return reply.Rows.Select(FromDto).ToArray();
    }

    private async ValueTask<IReadOnlyList<StartupActorAffinityRecord>> ReadCatalogShardAsync(
        int shardId,
        ClusterMember member,
        NodeReference authority,
        ClusterMembershipSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (member.Reference.Node == localNode!.NodeId)
        {
            var local = catalog[shardId];
            return local.FenceAndSnapshot(authority, snapshot.View);
        }
        var client = await clients!.GetClientAsync(
            new RouteLocation(new RouteKey("startup-affinity-catalog"), member.Reference, snapshot.View, member.ClusterEndpoint),
            cancellationToken).ConfigureAwait(false);
        var request = Request(ActorId.From($"@startup-affinity-shard/{shardId}"));
        request.Shard = shardId;
        StampAuthority(request, authority, snapshot.View);
        var reply = await client.CallAsync(CatalogLookupRpc, request, cancellationToken).ConfigureAwait(false);
        return reply.Rows.Select(FromDto).ToArray();
    }

    private async ValueTask RetainAsync(ActorId id, NodeReference target, long generation, ClusterMembershipSnapshot snapshot, CancellationToken ct)
    {
        if (target.Node == localNode!.NodeId)
        {
            var localAuthority = ActorLocationLayout.GetOwner(ActorLocationLayout.GetShard(id), snapshot)
                ?? throw new ActorDirectoryUnavailableException("Startup affinity has no Ready owner.");
            var local = catalog[ActorLocationLayout.GetShard(id)];
            local.FencedBind(localAuthority, snapshot.View, id, target, generation);
            return;
        }
        var member = snapshot.Members.Single(value => value.Reference == target);
        var client = await clients!.GetClientAsync(new RouteLocation(new RouteKey("startup-affinity-catalog"), target, snapshot.View, member.ClusterEndpoint), ct).ConfigureAwait(false);
        var request = Request(id, target); request.Generation = generation;
        var authority = ActorLocationLayout.GetOwner(ActorLocationLayout.GetShard(id), snapshot)
            ?? throw new ActorDirectoryUnavailableException("Startup affinity has no Ready owner.");
        StampAuthority(request, authority, snapshot.View);
        await client.CallAsync(RetainRpc, request, ct).ConfigureAwait(false);
    }

    private static void StampAuthority(AffinityRequest request, NodeReference authority, MembershipViewId view)
    {
        request.View = view.Value;
        request.AuthorityCluster = authority.Cluster.Value;
        request.AuthorityNode = authority.Node.Value;
        request.AuthorityIncarnation = authority.Incarnation.Value;
    }

    private StartupActorAffinityRecord? LocalLookup(ActorId id) => shards[ActorLocationLayout.GetShard(id)].Lookup(id);
    private StartupActorAffinityRecord LocalBind(ActorId id, NodeReference target, long generation = 1, bool pending = false)
    {
        var shard = shards[ActorLocationLayout.GetShard(id)];
        var existing = shard.Lookup(id);
        if (existing is not null && existing.Target != target)
            generation = Math.Max(generation, existing.Generation + 1);
        return shard.Bind(id, target, generation, pending);
    }

    internal static void Bind(RpcServiceRegistry registry, StartupActorAffinityDirectory directory)
    {
        var service = registry.RegisterSingleton(ClusterProtocol.ServiceId, directory, serviceName: "StartupAffinity");
        service.Register<AffinityRequest, AffinityReply>(LookupId, static (d, r, ct) => d.HandleAsync(LookupRpc, r, ct), "Lookup");
        service.Register<AffinityRequest, AffinityReply>(BindId, static (d, r, ct) => d.HandleAsync(BindRpc, r, ct), "Bind");
        service.Register<AffinityRequest, AffinityReply>(CatalogLookupId, static (d, r, ct) => d.HandleAsync(CatalogLookupRpc, r, ct), "CatalogLookup");
        service.Register<AffinityRequest, AffinityReply>(RetainId, static (d, r, ct) => d.HandleAsync(RetainRpc, r, ct), "Retain");
        service.Register<AffinityRequest, AffinityReply>(OwnerSnapshotId, static (d, r, ct) => d.HandleAsync(OwnerSnapshotRpc, r, ct), "OwnerSnapshot");
    }

    private static AffinityRequest Request(ActorId id, NodeReference? target = null) => new() { Id = id.Value, Cluster = target?.Cluster.Value ?? Guid.Empty, Node = target?.Node.Value ?? "", Incarnation = target?.Incarnation.Value ?? Guid.Empty };
    private static NodeReference Target(AffinityRequest value) => new(new(value.Cluster), new(value.Node), new(value.Incarnation));
    private static NodeReference Authority(AffinityRequest value) => new(new(value.AuthorityCluster), new(value.AuthorityNode), new(value.AuthorityIncarnation));
    private static AffinityReply Reply(StartupActorAffinityRecord? value, bool applied) => new() { Found = value is not null, Applied = applied, Cluster = value?.Target.Cluster.Value ?? Guid.Empty, Node = value?.Target.Node.Value ?? "", Incarnation = value?.Target.Incarnation.Value ?? Guid.Empty, Generation = value?.Generation ?? 0, Pending = value?.Pending ?? false };
    private static AffinityReply Reply(IReadOnlyList<StartupActorAffinityRecord> values) => new()
    {
        Rows = values.Select(value => new AffinityRow
        {
            Id = value.AffinityId.Value,
            Cluster = value.Target.Cluster.Value,
            Node = value.Target.Node.Value,
            Incarnation = value.Target.Incarnation.Value,
            Generation = value.Generation,
            Pending = value.Pending
        }).ToArray()
    };
    private static StartupActorAffinityRecord? FromReply(ActorId id, AffinityReply reply) => !reply.Found ? null : new(id, new(new(reply.Cluster), new(reply.Node), new(reply.Incarnation)), reply.Generation, reply.Pending);
    private static StartupActorAffinityRecord FromDto(AffinityRow value) => new(ActorId.From(value.Id), new(new(value.Cluster), new(value.Node), new(value.Incarnation)), value.Generation, value.Pending);

    internal sealed class AffinityShard
    {
        private readonly object gate = new();
        private readonly Dictionary<ActorId, StartupActorAffinityRecord> records = new();
        private NodeReference? authority;
        private MembershipViewId authorityView;
        private NodeReference? sealedForOwner;
        private MembershipViewId sealedAtView;
        private StartupActorAffinityRecord[]? sealedSnapshot;

        public bool TryAdvance(NodeReference owner, MembershipViewId view)
        {
            lock (gate)
            {
                if (authority != owner || view.Value > authorityView.Value + 1) return false;
                if (view.Value > authorityView.Value) authorityView = view;
                return true;
            }
        }

        public StartupActorAffinityRecord? FenceAndLookup(NodeReference owner, MembershipViewId view, ActorId id)
        {
            lock (gate)
            {
                FenceUnderLock(owner, view);
                return records.GetValueOrDefault(id);
            }
        }

        public IReadOnlyList<StartupActorAffinityRecord> FenceAndSnapshot(NodeReference owner, MembershipViewId view)
        {
            lock (gate)
            {
                FenceUnderLock(owner, view);
                return records.Values.ToArray();
            }
        }

        public StartupActorAffinityRecord FencedBind(NodeReference owner, MembershipViewId view, ActorId id, NodeReference target, long generation)
        {
            lock (gate)
            {
                FenceUnderLock(owner, view);
                return BindUnderLock(id, target, generation, pending: false);
            }
        }

        private void FenceUnderLock(NodeReference owner, MembershipViewId view)
        {
            if (view.Value < authorityView.Value || (view.Value == authorityView.Value && authority is not null && authority != owner))
                throw new ActorDirectoryUnavailableException("Startup affinity catalog authority is stale.");
            authority = owner;
            authorityView = view;
        }

        public void Activate(NodeReference owner, MembershipViewId view, IReadOnlyList<StartupActorAffinityRecord> recovered)
        {
            lock (gate)
            {
                if (recovered.Count > MaximumRowsPerShard)
                    throw new StartupActorSelectionException(typeof(IActor), "Startup Actor affinity shard capacity is exhausted during recovery.");
                records.Clear();
                foreach (var row in recovered) records.Add(row.AffinityId, row);
                authority = owner;
                authorityView = view;
                sealedForOwner = null;
                sealedSnapshot = null;
            }
        }

        public IReadOnlyList<StartupActorAffinityRecord> HandoffSnapshot(NodeReference newOwner, MembershipViewId view)
        {
            lock (gate)
            {
                if (sealedForOwner == newOwner && sealedSnapshot is not null)
                {
                    if (view.Value < sealedAtView.Value)
                        throw new ActorDirectoryUnavailableException("Startup affinity owner handoff retry is stale.");
                    return sealedSnapshot;
                }
                if (authority is null || authority == newOwner) return [];
                if (view.Value < authorityView.Value)
                    throw new ActorDirectoryUnavailableException("Startup affinity owner handoff view is stale.");
                sealedForOwner = newOwner;
                sealedAtView = view;
                sealedSnapshot = records.Values.ToArray();
                authority = newOwner;
                authorityView = view;
                return sealedSnapshot;
            }
        }

        public IReadOnlyList<StartupActorAffinityRecord> Snapshot()
        {
            lock (gate) return records.Values.ToArray();
        }

        public StartupActorAffinityRecord? Lookup(ActorId id)
        {
            lock (gate) return records.GetValueOrDefault(id);
        }

        public StartupActorAffinityRecord Bind(ActorId id, NodeReference target, long generation, bool pending = false)
        {
            lock (gate) return BindUnderLock(id, target, generation, pending);
        }

        public StartupActorAffinityRecord ReplacePendingTarget(
            ActorId id,
            NodeReference expectedTarget,
            NodeReference replacement)
        {
            lock (gate)
            {
                if (!records.TryGetValue(id, out var existing)
                    || !existing.Pending
                    || existing.Target != expectedTarget)
                    throw new ActorDirectoryUnavailableException(
                        $"Startup affinity Pending generation changed for '{id.Value}'.");
                return BindUnderLock(id, replacement, checked(existing.Generation + 1), pending: true);
            }
        }

        private StartupActorAffinityRecord BindUnderLock(ActorId id, NodeReference target, long generation, bool pending)
        {
            if (sealedForOwner is not null)
                throw new ActorDirectoryUnavailableException("Startup affinity shard is sealed for handoff.");
            if (records.TryGetValue(id, out var existing))
            {
                if (existing.Generation > generation) return existing;
                if (existing.Generation == generation && existing.Target != target)
                    throw new ActorDirectoryUnavailableException($"Conflicting Startup affinity generation for '{id.Value}'.");
                if (existing.Generation == generation && (!existing.Pending || pending)) return existing;
            }
            else if (records.Count >= MaximumRowsPerShard)
            {
                throw new StartupActorSelectionException(typeof(IActor), "Startup Actor affinity shard capacity is exhausted.");
            }
            var record = new StartupActorAffinityRecord(id, target, generation, pending);
            records[id] = record;
            return record;
        }

        public void Restore(StartupActorAffinityRecord record)
        {
            lock (gate)
            {
                if (!records.ContainsKey(record.AffinityId) && records.Count >= MaximumRowsPerShard)
                    throw new StartupActorSelectionException(typeof(IActor), "Startup Actor affinity shard capacity is exhausted during recovery.");
                if (records.TryGetValue(record.AffinityId, out var existing)
                    && existing.Generation == record.Generation && existing.Target != record.Target)
                    throw new ActorDirectoryUnavailableException($"Conflicting Startup affinity generation for '{record.AffinityId.Value}'.");
                if (existing is null || existing.Generation < record.Generation)
                    records[record.AffinityId] = record;
            }
        }
    }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class AffinityRequest
{
    [MemoryPackOrder(0)] public string Id { get; set; } = "";
    [MemoryPackOrder(1)] public long View { get; set; }
    [MemoryPackOrder(2)] public Guid Cluster { get; set; }
    [MemoryPackOrder(3)] public string Node { get; set; } = "";
    [MemoryPackOrder(4)] public Guid Incarnation { get; set; }
    [MemoryPackOrder(5)] public long Generation { get; set; }
    [MemoryPackOrder(6)] public Guid AuthorityCluster { get; set; }
    [MemoryPackOrder(7)] public string AuthorityNode { get; set; } = "";
    [MemoryPackOrder(8)] public Guid AuthorityIncarnation { get; set; }
    [MemoryPackOrder(9)] public string ActorName { get; set; } = "";
    [MemoryPackOrder(10)] public string PolicyHash { get; set; } = "";
    [MemoryPackOrder(11)] public string BuildTag { get; set; } = "";
    [MemoryPackOrder(12)] public int Shard { get; set; } = -1;
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class AffinityReply
{
    [MemoryPackOrder(0)] public bool Found { get; set; }
    [MemoryPackOrder(1)] public bool Applied { get; set; }
    [MemoryPackOrder(2)] public Guid Cluster { get; set; }
    [MemoryPackOrder(3)] public string Node { get; set; } = "";
    [MemoryPackOrder(4)] public Guid Incarnation { get; set; }
    [MemoryPackOrder(5)] public long Generation { get; set; }
    [MemoryPackOrder(6)] public IReadOnlyList<AffinityRow> Rows { get; set; } = Array.Empty<AffinityRow>();
    [MemoryPackOrder(7)] public bool Pending { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class AffinityRow
{
    [MemoryPackOrder(0)] public string Id { get; set; } = "";
    [MemoryPackOrder(1)] public Guid Cluster { get; set; }
    [MemoryPackOrder(2)] public string Node { get; set; } = "";
    [MemoryPackOrder(3)] public Guid Incarnation { get; set; }
    [MemoryPackOrder(4)] public long Generation { get; set; }
    [MemoryPackOrder(5)] public bool Pending { get; set; }
}
