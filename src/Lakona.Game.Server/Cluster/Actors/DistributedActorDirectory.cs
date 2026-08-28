using Lakona.Game.Cluster.Membership;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Actors.Internal;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Cluster.Actors;

internal sealed class DistributedActorDirectory :
    BackgroundService,
    IActorDirectory,
    IActorActivationPopulationSource
{
    private const int SnapshotPageSize = 256;
    private const int MaximumRetainedActivationSnapshots = 64;
    private const int MaximumOperationAttempts = 8;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);
    private readonly object viewGate = new();
    private readonly object activationSnapshotGate = new();
    private readonly SemaphoreSlim installGate = new(1, 1);
    private readonly IClusterMembership membership;
    private readonly IClusterClientFactory clients;
    private readonly LocalActorNodeIdentity localNode;
    private readonly IActorActivationSnapshotSource activationCatalog;
    private readonly IClusterMembershipRefresher? membershipRefresher;
    private readonly ILogger<DistributedActorDirectory>? logger;
    private readonly CancellationTokenSource stopping = new();
    private int disposed;
    private NodeReference? localReference;
    private ActorDirectoryPartition[]? partitions;
    private ActorDirectoryRing? currentRing;
    private readonly Dictionary<Guid, ActivationSnapshotSession> activationSnapshots = [];
    private long activationSnapshotSequence;

    public DistributedActorDirectory(
        IClusterMembership membership,
        IClusterClientFactory clients,
        LocalActorNodeIdentity localNode,
        IActorActivationSnapshotSource? activationCatalog = null,
        IClusterMembershipRefresher? membershipRefresher = null,
        ILogger<DistributedActorDirectory>? logger = null)
    {
        this.membership = membership ?? throw new ArgumentNullException(nameof(membership));
        this.clients = clients ?? throw new ArgumentNullException(nameof(clients));
        this.localNode = localNode ?? throw new ArgumentNullException(nameof(localNode));
        this.activationCatalog = activationCatalog ?? EmptyActorActivationSnapshotSource.Instance;
        this.membershipRefresher = membershipRefresher;
        this.logger = logger;
    }

    internal CancellationToken StoppingToken => stopping.Token;

    ActorActivationPopulation IActorActivationPopulationSource.ObserveActivationPopulation()
    {
        var active = activationCatalog.ActiveCount;
        return new ActorActivationPopulation(active, active, 0);
    }

    public async ValueTask<ActorDirectoryRecord?> ResolveAsync(
        ActorId actorId,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(
                ActorDirectoryProtocol.Lookup,
                actorId,
                null,
                null,
                cancellationToken)
            .ConfigureAwait(false);
        return result.Record;
    }

    public async ValueTask<ActorActivationAcquireResult> AcquireAsync(
        ActorId actorId,
        NodeReference proposedOwner,
        ActorActivationId proposedActivation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposedOwner);
        var result = await ExecuteAsync(
                ActorDirectoryProtocol.Acquire,
                actorId,
                proposedOwner,
                proposedActivation,
                cancellationToken)
            .ConfigureAwait(false);
        return new ActorActivationAcquireResult(
            result.Record ?? throw new ActorDirectoryUnavailableException(
                "Actor registration returned no exact activation record."),
            result.Status == ActorDirectoryOperationStatus.Applied);
    }

    public async ValueTask<bool> ReleaseAsync(
        ActorId actorId,
        ActorActivationId expectedActivation,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(
                ActorDirectoryProtocol.Release,
                actorId,
                null,
                expectedActivation,
                cancellationToken)
            .ConfigureAwait(false);
        return result.Status == ActorDirectoryOperationStatus.Applied;
    }

    internal async ValueTask EnsureViewAsync(
        MembershipViewId requiredView,
        CancellationToken cancellationToken)
    {
        while (membership.Current.View.CompareTo(requiredView) < 0)
        {
            if (membershipRefresher is not null)
                await membershipRefresher.RefreshAsync(cancellationToken).ConfigureAwait(false);
            if (membership.Current.View.CompareTo(requiredView) >= 0) break;
            await membership.WaitForChangeAsync(membership.Current.View, cancellationToken)
                .ConfigureAwait(false);
        }

        await installGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var target = membership.Current;
            ActorDirectoryRing? previous;
            lock (viewGate)
            {
                previous = currentRing;
                if (previous is not null && previous.View.CompareTo(target.View) >= 0) return;
                EnsureLocalPartitions(target);
            }

            var current = new ActorDirectoryRing(target);
            ActorDirectoryPartition.PreparedTransition[] transitions;
            lock (viewGate)
            {
                transitions = partitions!
                    .Select(partition => partition.PrepareTransition(previous, current))
                    .ToArray();
                currentRing = current;
            }
            lock (activationSnapshotGate) activationSnapshots.Clear();

            // Every affected range is locked before any transfer can begin.
            foreach (var transition in transitions) transition.Start();
        }
        finally
        {
            installGate.Release();
        }
    }

    internal async ValueTask<IReadOnlyList<ActorDirectoryRecord>> TransferRangeAsync(
        ActorDirectoryRing previous,
        ActorDirectoryRing current,
        ActorDirectoryRange addedRange,
        CancellationToken cancellationToken)
    {
        var records = new Dictionary<ActorId, ActorDirectoryRecord>();
        var sources = previous.GetIntersectingPartitions(addedRange).ToArray();
        try
        {
            foreach (var source in sources)
            foreach (var intersection in source.Range.Intersections(addedRange))
            {
                var sourceMember = previous.FindMember(source.Partition.Owner)
                    ?? throw new ActorDirectoryUnavailableException(
                        "The previous Actor Directory owner is absent from its Membership view.");
                var page = await ReadPartitionSnapshotWithRetryAsync(
                        source.Partition,
                        sourceMember,
                        current.View,
                        previous.View,
                        intersection,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (page is null)
                    return await RecoverRangeAsync(current, addedRange, cancellationToken)
                        .ConfigureAwait(false);
                Merge(records, page);
            }

            return records.Values.ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return await RecoverRangeAsync(current, addedRange, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    internal async ValueTask AcknowledgeRangeAsync(
        ActorDirectoryRing? previous,
        ActorDirectoryRing current,
        ActorDirectoryPartitionId receiver,
        ActorDirectoryRange addedRange,
        CancellationToken cancellationToken)
    {
        if (previous is null || current.View.Value != previous.View.Value + 1) return;

        foreach (var source in previous.GetIntersectingPartitions(addedRange))
        {
            try
            {
                var sourceMember = previous.FindMember(source.Partition.Owner);
                if (sourceMember is null) continue;
                await AcknowledgeSnapshotAsync(
                        source.Partition,
                        sourceMember,
                        current.View,
                        previous.View,
                        receiver,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // The receiver already committed the records. A failed acknowledgement only
                // retains an extra snapshot, which is pruned after the next Membership view.
                logger?.LogDebug(
                    exception,
                    "Actor Directory snapshot acknowledgement to {Source} failed.",
                    source.Partition.Owner);
            }
        }
    }

    internal async ValueTask<IReadOnlyList<ActorDirectoryRecord>> RecoverRangeAsync(
        ActorDirectoryRing ring,
        ActorDirectoryRange range,
        CancellationToken cancellationToken)
    {
        var recovered = new Dictionary<ActorId, ActorDirectoryRecord>();
        foreach (var member in ring.Membership.Members.Where(static value =>
                     value.State == ClusterMemberState.Active))
        {
            IReadOnlyList<ActorDirectoryRecord> page;
            if (member.Reference == localReference)
            {
                page = activationCatalog.CaptureRecoveryClaims()
                    .Where(record => range.Contains(record.ActorId))
                    .ToArray();
            }
            else
            {
                page = await ReadActivationSnapshotWithRetryAsync(
                        member,
                        ring.View,
                        range,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            Merge(recovered, page);
        }

        return recovered.Values.ToArray();
    }

    internal async ValueTask<ActorDirectoryReply> HandleAsync(
        RpcMethod<ActorDirectoryRequest, ActorDirectoryReply> method,
        ActorDirectoryRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureViewAsync(new MembershipViewId(request.View), cancellationToken).ConfigureAwait(false);
        var actorId = ActorId.From(request.ActorId);
        ActorDirectoryRing ring;
        lock (viewGate) ring = currentRing!;
        var expected = ring.GetOwner(actorId);
        if (expected.Owner != localReference || expected.Index != request.PartitionIndex)
            return Reply(new ActorDirectoryOperationResult(
                ActorDirectoryOperationStatus.RefreshRequired,
                ring.View,
                null));

        var partition = partitions![expected.Index];
        ActorDirectoryOperationResult result;
        if (method.MethodId == ActorDirectoryProtocol.Lookup.MethodId)
        {
            result = await partition.LookupAsync(actorId, new MembershipViewId(request.View), cancellationToken)
                .ConfigureAwait(false);
        }
        else if (method.MethodId == ActorDirectoryProtocol.Acquire.MethodId)
        {
            result = await partition.AcquireAsync(
                    actorId,
                    Host(request),
                    new ActorActivationId(request.Activation),
                    new MembershipViewId(request.View),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else if (method.MethodId == ActorDirectoryProtocol.Release.MethodId)
        {
            result = await partition.ReleaseAsync(
                    actorId,
                    new ActorActivationId(request.Activation),
                    new MembershipViewId(request.View),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            throw new ActorDirectoryUnavailableException(
                $"Unknown Actor Directory method id '{method.MethodId}'.");
        }

        return Reply(result);
    }

    internal async ValueTask<ActorDirectorySnapshotReply> HandlePartitionSnapshotAsync(
        ActorDirectoryPartitionSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureViewAsync(new MembershipViewId(request.View), cancellationToken).ConfigureAwait(false);
        if ((uint)request.PartitionIndex >= ActorDirectoryRing.DefaultPartitionsPerNode)
            return new ActorDirectorySnapshotReply { Available = false, View = CurrentView.Value };
        var all = await partitions![request.PartitionIndex].GetSnapshotAsync(
                new MembershipViewId(request.View),
                new MembershipViewId(request.SnapshotView),
                Range(request.Range),
                cancellationToken)
            .ConfigureAwait(false);
        return Page(all, request.Offset);
    }

    internal async ValueTask<ActorDirectorySnapshotReply> HandleActivationSnapshotAsync(
        ActorDirectoryActivationSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureViewAsync(new MembershipViewId(request.View), cancellationToken).ConfigureAwait(false);
        var snapshot = membership.Current;
        if (snapshot.View.Value < request.View || localReference is null
            || !snapshot.Members.Any(member =>
                member.Reference == localReference && member.State == ClusterMemberState.Active))
            return new ActorDirectorySnapshotReply { Available = false, View = snapshot.View.Value };

        if (request.SnapshotId == Guid.Empty)
            return new ActorDirectorySnapshotReply { Available = false, View = snapshot.View.Value };

        var range = Range(request.Range);
        ActorDirectoryRecord[] records;
        lock (activationSnapshotGate)
        {
            if (request.Offset == 0)
            {
                records = activationCatalog.CaptureRecoveryClaims()
                    .Where(record => range.Contains(record.ActorId))
                    .OrderBy(static record => record.ActorId.Value, StringComparer.Ordinal)
                    .ToArray();
                RetainActivationSnapshot(request.SnapshotId, request.View, range, records);
            }
            else if (!activationSnapshots.TryGetValue(request.SnapshotId, out var retained)
                     || retained.View != request.View
                     || retained.Range != range)
            {
                return new ActorDirectorySnapshotReply { Available = false, View = snapshot.View.Value };
            }
            else
            {
                records = retained.Records;
            }

            var reply = Page(records, request.Offset);
            if (reply.Available && !reply.HasMore)
                activationSnapshots.Remove(request.SnapshotId);
            return reply;
        }
    }

    internal async ValueTask<ActorDirectoryAcknowledgeReply> HandleAcknowledgeAsync(
        ActorDirectorySnapshotAcknowledgeRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureViewAsync(new MembershipViewId(request.View), cancellationToken).ConfigureAwait(false);
        if ((uint)request.PartitionIndex >= ActorDirectoryRing.DefaultPartitionsPerNode)
            return new ActorDirectoryAcknowledgeReply { Applied = false, View = CurrentView.Value };
        partitions![request.PartitionIndex].AcknowledgeSnapshot(
            new MembershipViewId(request.SnapshotView),
            new ActorDirectoryPartitionId(
                new NodeReference(
                    new ClusterIncarnationId(request.ReceiverCluster),
                    new NodeId(request.ReceiverNode),
                    new NodeIncarnationId(request.ReceiverIncarnation)),
                request.ReceiverPartitionIndex));
        return new ActorDirectoryAcknowledgeReply { Applied = true, View = CurrentView.Value };
    }

    internal static void Bind(RpcServiceRegistry registry, DistributedActorDirectory directory)
    {
        var service = registry.RegisterSingleton(
            ClusterProtocol.ServiceId,
            directory,
            serviceName: "ActorDirectory");
        service.Register<ActorDirectoryRequest, ActorDirectoryReply>(
            ActorDirectoryProtocol.Lookup.MethodId,
            static (value, request, ct) => value.HandleAsync(ActorDirectoryProtocol.Lookup, request, ct),
            "Lookup");
        service.Register<ActorDirectoryRequest, ActorDirectoryReply>(
            ActorDirectoryProtocol.Acquire.MethodId,
            static (value, request, ct) => value.HandleAsync(ActorDirectoryProtocol.Acquire, request, ct),
            "Acquire");
        service.Register<ActorDirectoryRequest, ActorDirectoryReply>(
            ActorDirectoryProtocol.Release.MethodId,
            static (value, request, ct) => value.HandleAsync(ActorDirectoryProtocol.Release, request, ct),
            "Release");
        service.Register<ActorDirectoryActivationSnapshotRequest, ActorDirectorySnapshotReply>(
            ActorDirectoryProtocol.ActivationSnapshot.MethodId,
            static (value, request, ct) => value.HandleActivationSnapshotAsync(request, ct),
            "ActivationSnapshot");
        service.Register<ActorDirectoryPartitionSnapshotRequest, ActorDirectorySnapshotReply>(
            ActorDirectoryProtocol.PartitionSnapshot.MethodId,
            static (value, request, ct) => value.HandlePartitionSnapshotAsync(request, ct),
            "PartitionSnapshot");
        service.Register<ActorDirectorySnapshotAcknowledgeRequest, ActorDirectoryAcknowledgeReply>(
            ActorDirectoryProtocol.AcknowledgeSnapshot.MethodId,
            static (value, request, ct) => value.HandleAcknowledgeAsync(request, ct),
            "AcknowledgeSnapshot");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, stopping.Token);
        while (!linked.IsCancellationRequested)
        {
            try
            {
                await EnsureViewAsync(membership.Current.View, linked.Token).ConfigureAwait(false);
                var observed = CurrentView;
                await membership.WaitForChangeAsync(observed, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger?.LogWarning(exception, "Actor Directory could not process the latest Membership view.");
                await Task.Delay(RetryDelay, linked.Token).ConfigureAwait(false);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await stopping.CancelAsync().ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        lock (activationSnapshotGate) activationSnapshots.Clear();
    }

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;

        stopping.Cancel();
        lock (activationSnapshotGate) activationSnapshots.Clear();
        stopping.Dispose();
        installGate.Dispose();
        base.Dispose();
    }

    private MembershipViewId CurrentView
    {
        get
        {
            lock (viewGate) return currentRing?.View ?? default;
        }
    }

    private async ValueTask<ActorDirectoryOperationResult> ExecuteAsync(
        RpcMethod<ActorDirectoryRequest, ActorDirectoryReply> method,
        ActorId actorId,
        NodeReference? proposedOwner,
        ActorActivationId? activation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumOperationAttempts; attempt++)
        {
            await EnsureViewAsync(membership.Current.View, cancellationToken).ConfigureAwait(false);
            ActorDirectoryRing ring;
            lock (viewGate) ring = currentRing!;
            var owner = ring.GetOwner(actorId);
            var request = new ActorDirectoryRequest
            {
                ActorId = actorId.Value,
                View = ring.View.Value,
                PartitionIndex = owner.Index,
                HostCluster = proposedOwner?.Cluster.Value ?? Guid.Empty,
                HostNode = proposedOwner?.Node.Value ?? string.Empty,
                HostIncarnation = proposedOwner?.Incarnation.Value ?? Guid.Empty,
                Activation = activation?.Value ?? Guid.Empty
            };

            ActorDirectoryReply reply;
            if (owner.Owner == localReference)
            {
                reply = await HandleAsync(method, request, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var member = ring.FindMember(owner.Owner)
                    ?? throw new ActorDirectoryUnavailableException("Actor Directory owner left Membership.");
                var client = await clients.GetClientAsync(
                        new RouteLocation(
                            new RouteKey("actor-directory"),
                            owner.Owner,
                            ring.View,
                            member.ClusterEndpoint),
                        cancellationToken)
                    .ConfigureAwait(false);
                reply = await client.CallAsync(method, request, cancellationToken).ConfigureAwait(false);
            }

            var result = Result(reply);
            if (result.Status != ActorDirectoryOperationStatus.RefreshRequired) return result;
            if (membershipRefresher is not null)
                await membershipRefresher.RefreshAsync(cancellationToken).ConfigureAwait(false);
            if (reply.View > ring.View.Value)
                await EnsureViewAsync(new MembershipViewId(reply.View), cancellationToken).ConfigureAwait(false);
        }

        throw new ActorDirectoryUnavailableException(
            "Actor Directory could not converge on one range owner.");
    }

    private async ValueTask<IReadOnlyList<ActorDirectoryRecord>?> ReadPartitionSnapshotAsync(
        ActorDirectoryPartitionId source,
        ClusterMember sourceMember,
        MembershipViewId requestView,
        MembershipViewId snapshotView,
        ActorDirectoryRange range,
        CancellationToken cancellationToken)
    {
        if (source.Owner == localReference)
            return await partitions![source.Index].GetSnapshotAsync(
                    requestView,
                    snapshotView,
                    range,
                    cancellationToken)
                .ConfigureAwait(false);

        var client = await clients.GetClientAsync(
                new RouteLocation(
                    new RouteKey("actor-directory-snapshot"),
                    source.Owner,
                    requestView,
                    sourceMember.ClusterEndpoint),
                cancellationToken)
            .ConfigureAwait(false);
        var records = new List<ActorDirectoryRecord>();
        var actorIds = new HashSet<ActorId>();
        int? totalCount = null;
        for (var offset = 0;; offset += SnapshotPageSize)
        {
            var reply = await client.CallAsync(
                    ActorDirectoryProtocol.PartitionSnapshot,
                    new ActorDirectoryPartitionSnapshotRequest
                    {
                        View = requestView.Value,
                        SnapshotView = snapshotView.Value,
                        PartitionIndex = source.Index,
                        Range = Dto(range),
                        Offset = offset
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (reply.View < requestView.Value)
                throw new ActorDirectoryUnavailableException(
                    $"Actor Directory snapshot replied from stale Membership view '{reply.View}' "
                    + $"while view '{requestView.Value}' was required.");
            if (!reply.Available) return null;
            if (reply.TotalCount < 0 || totalCount is not null && totalCount != reply.TotalCount)
                throw new ActorDirectoryUnavailableException(
                    "Actor Directory snapshot returned an inconsistent total record count.");
            totalCount = reply.TotalCount;
            var consumed = offset + reply.Records.Count;
            if (reply.Records.Count > SnapshotPageSize
                || reply.HasMore && (reply.Records.Count != SnapshotPageSize || consumed >= totalCount)
                || !reply.HasMore && consumed != totalCount)
                throw new ActorDirectoryUnavailableException(
                    "Actor Directory snapshot returned an incomplete page sequence.");
            foreach (var dto in reply.Records)
            {
                var record = Record(dto);
                if (!actorIds.Add(record.ActorId))
                    throw new ActorDirectoryUnavailableException(
                        $"Actor Directory snapshot repeated Actor '{record.ActorId.Value}'.");
                records.Add(record);
            }
            if (!reply.HasMore) return records;
        }
    }

    private async ValueTask<IReadOnlyList<ActorDirectoryRecord>?> ReadPartitionSnapshotWithRetryAsync(
        ActorDirectoryPartitionId source,
        ClusterMember sourceMember,
        MembershipViewId requestView,
        MembershipViewId snapshotView,
        ActorDirectoryRange range,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                return await ReadPartitionSnapshotAsync(
                        source,
                        sourceMember,
                        requestView,
                        snapshotView,
                        range,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                var latest = membership.Current;
                if (!latest.Members.Any(member =>
                    member.State == ClusterMemberState.Active
                    && member.Reference == source.Owner))
                    return null;
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask AcknowledgeSnapshotAsync(
        ActorDirectoryPartitionId source,
        ClusterMember sourceMember,
        MembershipViewId requestView,
        MembershipViewId snapshotView,
        ActorDirectoryPartitionId receiver,
        CancellationToken cancellationToken)
    {
        if (source.Owner == localReference)
        {
            partitions![source.Index].AcknowledgeSnapshot(snapshotView, receiver);
            return;
        }

        var client = await clients.GetClientAsync(
                new RouteLocation(
                    new RouteKey("actor-directory-ack"),
                    source.Owner,
                    requestView,
                    sourceMember.ClusterEndpoint),
                cancellationToken)
            .ConfigureAwait(false);
        await client.CallAsync(
                ActorDirectoryProtocol.AcknowledgeSnapshot,
                new ActorDirectorySnapshotAcknowledgeRequest
                {
                    View = requestView.Value,
                    SnapshotView = snapshotView.Value,
                    PartitionIndex = source.Index,
                    ReceiverCluster = receiver.Owner.Cluster.Value,
                    ReceiverNode = receiver.Owner.Node.Value,
                    ReceiverIncarnation = receiver.Owner.Incarnation.Value,
                    ReceiverPartitionIndex = receiver.Index
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyList<ActorDirectoryRecord>> ReadActivationSnapshotWithRetryAsync(
        ClusterMember member,
        MembershipViewId view,
        ActorDirectoryRange range,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                var client = await clients.GetClientAsync(
                        new RouteLocation(
                            new RouteKey("actor-directory-recovery"),
                            member.Reference,
                            view,
                            member.ClusterEndpoint),
                        cancellationToken)
                    .ConfigureAwait(false);
                var records = new List<ActorDirectoryRecord>();
                int? totalCount = null;
                var snapshotId = Guid.NewGuid();
                for (var offset = 0;; offset += SnapshotPageSize)
                {
                    var reply = await client.CallAsync(
                            ActorDirectoryProtocol.ActivationSnapshot,
                            new ActorDirectoryActivationSnapshotRequest
                            {
                                View = view.Value,
                                Range = Dto(range),
                                Offset = offset,
                                SnapshotId = snapshotId
                            },
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (reply.View < view.Value)
                        throw new ActorDirectoryUnavailableException(
                            $"Actor activation catalog on '{member.Reference}' replied from stale "
                            + $"Membership view '{reply.View}' while view '{view.Value}' was required.");
                    if (!reply.Available)
                        throw new ActorDirectoryUnavailableException(
                            $"Actor activation catalog on '{member.Reference}' is not ready for recovery.");
                    if (reply.TotalCount < 0 || totalCount is not null && totalCount != reply.TotalCount)
                        throw new ActorDirectoryUnavailableException(
                            $"Actor activation catalog on '{member.Reference}' returned an inconsistent "
                            + "total record count.");
                    totalCount = reply.TotalCount;
                    var consumed = offset + reply.Records.Count;
                    if (reply.Records.Count > SnapshotPageSize
                        || reply.HasMore && (reply.Records.Count != SnapshotPageSize || consumed >= totalCount)
                        || !reply.HasMore && consumed != totalCount)
                        throw new ActorDirectoryUnavailableException(
                            $"Actor activation catalog on '{member.Reference}' returned an incomplete "
                            + "page sequence.");
                    records.AddRange(reply.Records.Select(Record));
                    if (!reply.HasMore) return records;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                var latest = membership.Current;
                if (!latest.Members.Any(value =>
                    value.State == ClusterMemberState.Active && value.Reference == member.Reference))
                    return [];
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void EnsureLocalPartitions(ClusterMembershipSnapshot snapshot)
    {
        var exact = snapshot.Members.SingleOrDefault(member => member.Reference.Node == localNode.NodeId)
            ?.Reference;
        if (localReference is null)
        {
            localReference = exact ?? throw new ActorDirectoryUnavailableException(
                $"Local node '{localNode.NodeId.Value}' is absent from Membership.");
            partitions = Enumerable.Range(0, ActorDirectoryRing.DefaultPartitionsPerNode)
                .Select(index => new ActorDirectoryPartition(
                    new ActorDirectoryPartitionId(localReference, index),
                    this))
                .ToArray();
            return;
        }

        if (exact is not null && exact != localReference)
            throw new ActorDirectoryUnavailableException(
                $"Local node '{localNode.NodeId.Value}' was replaced by another process incarnation.");
    }

    private static void Merge(
        Dictionary<ActorId, ActorDirectoryRecord> target,
        IReadOnlyList<ActorDirectoryRecord> source)
    {
        foreach (var record in source)
        {
            if (target.TryGetValue(record.ActorId, out var existing)
                && (existing.OwnerReference != record.OwnerReference
                    || existing.ActivationId != record.ActivationId))
            {
                throw new ActorDirectoryUnavailableException(
                    $"Conflicting live activations were recovered for '{record.ActorId.Value}'.");
            }

            target[record.ActorId] = record;
        }
    }

    private ActorDirectorySnapshotReply Page(
        IReadOnlyList<ActorDirectoryRecord>? records,
        int offset)
    {
        if (records is null)
            return new ActorDirectorySnapshotReply { Available = false, View = CurrentView.Value };
        if (offset < 0 || offset > records.Count)
            return new ActorDirectorySnapshotReply { Available = false, View = CurrentView.Value };
        var page = records.Skip(offset).Take(SnapshotPageSize).Select(Dto).ToArray();
        return new ActorDirectorySnapshotReply
        {
            Available = true,
            View = CurrentView.Value,
            Records = page,
            HasMore = offset + page.Length < records.Count,
            TotalCount = records.Count
        };
    }

    private void RetainActivationSnapshot(
        Guid snapshotId,
        long view,
        ActorDirectoryRange range,
        ActorDirectoryRecord[] records)
    {
        if (!activationSnapshots.ContainsKey(snapshotId)
            && activationSnapshots.Count >= MaximumRetainedActivationSnapshots)
        {
            var oldest = activationSnapshots.MinBy(static pair => pair.Value.Sequence).Key;
            activationSnapshots.Remove(oldest);
        }

        activationSnapshots[snapshotId] = new ActivationSnapshotSession(
            view,
            range,
            records,
            checked(++activationSnapshotSequence));
    }

    private static ActorDirectoryReply Reply(ActorDirectoryOperationResult result) => new()
    {
        Status = (int)result.Status,
        View = result.View.Value,
        Record = result.Record is null ? null : Dto(result.Record)
    };

    private static ActorDirectoryOperationResult Result(ActorDirectoryReply reply) => new(
        Enum.IsDefined(typeof(ActorDirectoryOperationStatus), reply.Status)
            ? (ActorDirectoryOperationStatus)reply.Status
            : ActorDirectoryOperationStatus.Unavailable,
        new MembershipViewId(reply.View),
        reply.Record is null ? null : Record(reply.Record));

    private static ActorDirectoryRecordDto Dto(ActorDirectoryRecord record) => new()
    {
        ActorId = record.ActorId.Value,
        HostCluster = record.OwnerReference!.Cluster.Value,
        HostNode = record.OwnerReference.Node.Value,
        HostIncarnation = record.OwnerReference.Incarnation.Value,
        Activation = record.ActivationId.Value
    };

    private static ActorDirectoryRecord Record(ActorDirectoryRecordDto value) => new(
        ActorId.From(value.ActorId),
        new NodeReference(
            new ClusterIncarnationId(value.HostCluster),
            new NodeId(value.HostNode),
            new NodeIncarnationId(value.HostIncarnation)),
        new ActorActivationId(value.Activation),
        DateTimeOffset.UtcNow);

    private static NodeReference Host(ActorDirectoryRequest request) => new(
        new ClusterIncarnationId(request.HostCluster),
        new NodeId(request.HostNode),
        new NodeIncarnationId(request.HostIncarnation));

    private static ActorDirectoryRangeDto Dto(ActorDirectoryRange range) => new()
    {
        Start = range.Start,
        End = range.End,
        Kind = range.IsFull ? 2 : range.IsEmpty ? 0 : 1
    };

    private static ActorDirectoryRange Range(ActorDirectoryRangeDto value) => value.Kind switch
    {
        0 => ActorDirectoryRange.Empty,
        1 => ActorDirectoryRange.Create(value.Start, value.End),
        2 => ActorDirectoryRange.Full,
        _ => throw new ActorDirectoryUnavailableException("Actor Directory received an invalid hash range.")
    };

    private sealed class EmptyActorActivationSnapshotSource : IActorActivationSnapshotSource
    {
        public static EmptyActorActivationSnapshotSource Instance { get; } = new();

        public IReadOnlyList<ActorDirectoryRecord> CaptureRecoveryClaims() => [];

        public int ActiveCount => 0;
    }

    private sealed record ActivationSnapshotSession(
        long View,
        ActorDirectoryRange Range,
        ActorDirectoryRecord[] Records,
        long Sequence);
}
