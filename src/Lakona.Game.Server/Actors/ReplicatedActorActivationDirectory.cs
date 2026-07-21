using System.Text;
using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Hosting;

namespace Lakona.Game.Server.Actors;

public sealed class ReplicatedActorActivationDirectory :
    IActorDirectory,
    IActorActivationDirectory,
    IClusterMessageHandler
{
    private const int PartitionCount = 1024;
    private const int ReplicaCount = 3;
    private const string ResolveKind = "_activation_resolve_v1";
    private const string ReplicaResolveKind = "_activation_replica_resolve_v1";
    private const string AcquireKind = "_activation_acquire_v1";
    private const string ReplicateAcquireKind = "_activation_replicate_acquire_v1";
    private const string ReleaseKind = "_activation_release_v1";
    private const string ReplicateReleaseKind = "_activation_replicate_release_v1";
    private static readonly RouteKey Route = new("actor-activation:partition");

    private readonly InMemoryActorDirectory replica = new();
    private readonly IClusterMembership membership;
    private readonly IExactClusterNodeSender exactSender;
    private readonly IClusterNodeSender replySender;
    private readonly RemoteActorGateway gateway;
    private readonly LocalActorNodeIdentity localNode;
    private readonly TimeSpan timeout;
    private readonly IDistributedWorkAdmissionGate? admissionGate;

    public ReplicatedActorActivationDirectory(
        IClusterMembership membership,
        IExactClusterNodeSender exactSender,
        IClusterNodeSender replySender,
        RemoteActorGateway gateway,
        LocalActorNodeIdentity localNode,
        RemoteActorOptions? options = null,
        IDistributedWorkAdmissionGate? admissionGate = null)
    {
        this.membership = membership;
        this.exactSender = exactSender;
        this.replySender = replySender;
        this.gateway = gateway;
        this.localNode = localNode;
        this.admissionGate = admissionGate;
        timeout = (options ?? new RemoteActorOptions()).DefaultTimeout;
    }

    public async ValueTask<ActorDirectoryRecord?> ResolveAsync(
        ActorId actorId,
        CancellationToken cancellationToken = default)
    {
        var admission = EnterAdmission();
        try
        {
            var reply = await ExecuteAtPrimaryAsync(
                ResolveKind,
                new ActivationRequest { ActorId = actorId.Value },
                cancellationToken).ConfigureAwait(false);
            return reply.Record is null ? null : FromDto(reply.Record);
        }
        finally
        {
            ExitAdmission(admission);
        }
    }

    public async ValueTask<ActorDirectoryRegisterStatus> RegisterAsync(
        ActorId actorId,
        NodeId node,
        CancellationToken cancellationToken = default)
    {
        var snapshot = membership.Current;
        var member = snapshot.Members.SingleOrDefault(item =>
            item.Reference.Node == node && item.State == ClusterMemberState.Ready);
        if (member is null)
        {
            return ActorDirectoryRegisterStatus.Conflict;
        }

        var result = await AcquireAsync(
            actorId,
            member.Reference,
            ActorActivationId.New(),
            cancellationToken).ConfigureAwait(false);
        return result.Acquired
            ? ActorDirectoryRegisterStatus.Registered
            : result.Record.Node == node
                ? ActorDirectoryRegisterStatus.AlreadyRegistered
                : ActorDirectoryRegisterStatus.Conflict;
    }

    public async ValueTask<ActorDirectoryUnregisterStatus> UnregisterAsync(
        ActorId actorId,
        NodeId node,
        CancellationToken cancellationToken = default)
    {
        var existing = await ResolveAsync(actorId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return ActorDirectoryUnregisterStatus.NotFound;
        }

        if (existing.Node != node
            || existing.ActivationId is not ActorActivationId activation)
        {
            return ActorDirectoryUnregisterStatus.OwnershipMismatch;
        }

        return await ReleaseAsync(
            actorId,
            activation,
            existing.Version,
            cancellationToken).ConfigureAwait(false)
            ? ActorDirectoryUnregisterStatus.Unregistered
            : ActorDirectoryUnregisterStatus.OwnershipMismatch;
    }

    public async ValueTask<ActorActivationAcquireResult> AcquireAsync(
        ActorId actorId,
        NodeReference proposedOwner,
        ActorActivationId proposedActivation,
        CancellationToken cancellationToken = default)
    {
        var admission = EnterAdmission();
        try
        {
            var reply = await ExecuteAtPrimaryAsync(
                AcquireKind,
                ActivationRequest.ForAcquire(actorId, proposedOwner, proposedActivation),
                cancellationToken).ConfigureAwait(false);
            if (reply.Record is null || !reply.Succeeded)
            {
                throw new ActorDirectoryUnavailableException(
                    reply.Error ?? "Activation acquire did not reach a replica majority.");
            }

            return new ActorActivationAcquireResult(FromDto(reply.Record), reply.Changed);
        }
        finally
        {
            ExitAdmission(admission);
        }
    }

    public async ValueTask<bool> ReleaseAsync(
        ActorId actorId,
        ActorActivationId expectedActivation,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var admission = EnterAdmission();
        try
        {
            var reply = await ExecuteAtPrimaryAsync(
                ReleaseKind,
                new ActivationRequest
                {
                    ActorId = actorId.Value,
                    Activation = expectedActivation.Value,
                    Version = expectedVersion
                },
                cancellationToken).ConfigureAwait(false);
            return reply.Succeeded && reply.Changed;
        }
        finally
        {
            ExitAdmission(admission);
        }
    }

    public async ValueTask<ClusterSendStatus> HandleAsync(
        ClusterMessage message,
        CancellationToken cancellationToken = default)
    {
        if (message.Kind is not (ResolveKind or ReplicaResolveKind or AcquireKind or ReplicateAcquireKind
            or ReleaseKind or ReplicateReleaseKind))
        {
            return ClusterSendStatus.RouteNotFound;
        }

        if (string.IsNullOrWhiteSpace(message.CorrelationId))
        {
            return ClusterSendStatus.Rejected;
        }

        DistributedWorkAdmission admission = default;
        if (admissionGate is not null && !admissionGate.TryEnter(out admission))
        {
            return ClusterSendStatus.Rejected;
        }

        try
        {
            ActivationReply reply;
            try
            {
                var request = JsonSerializer.Deserialize<ActivationRequest>(message.Payload.Span)
                    ?? throw new InvalidOperationException("Activation request is empty.");
                reply = await ExecuteLocalAsync(message.Kind, request, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                reply = new ActivationReply { Error = exception.Message };
            }

            return await RemoteActorGateway.SendReplyAsync(
                replySender,
                localNode.NodeId,
                message.SourceNode,
                message.CorrelationId,
                JsonSerializer.SerializeToUtf8Bytes(reply),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ExitAdmission(admission);
        }
    }

    private DistributedWorkAdmission EnterAdmission()
    {
        if (admissionGate is null)
        {
            return default;
        }

        if (!admissionGate.TryEnter(out var admission))
        {
            throw new ActorDirectoryUnavailableException(
                "Distributed actor work is fenced because quorum authority is unavailable.");
        }

        return admission;
    }

    private void ExitAdmission(DistributedWorkAdmission admission)
    {
        if (admission.IsAdmitted)
        {
            admissionGate!.Exit(admission);
        }
    }

    private async ValueTask<ActivationReply> ExecuteAtPrimaryAsync(
        string kind,
        ActivationRequest request,
        CancellationToken cancellationToken)
    {
        var snapshot = membership.Current;
        var replicas = SelectReplicas(snapshot, request.ActorId);
        if (replicas.Count == 0)
        {
            throw new ActorDirectoryUnavailableException("No ready activation-directory replica exists.");
        }

        return replicas[0].Reference.Node == localNode.NodeId
            ? await ExecuteLocalAsync(kind, request, cancellationToken).ConfigureAwait(false)
            : await SendRequestAsync(replicas[0], snapshot.View, kind, request, cancellationToken)
                .ConfigureAwait(false);
    }

    private async ValueTask<ActivationReply> ExecuteLocalAsync(
        string kind,
        ActivationRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = ActorId.From(request.ActorId);
        if (kind == ReplicaResolveKind)
        {
            var record = await replica.ResolveAsync(actorId, cancellationToken).ConfigureAwait(false);
            return new ActivationReply { Succeeded = true, Record = record is null ? null : ToDto(record) };
        }

        if (kind == ResolveKind)
        {
            var snapshot = membership.Current;
            var replicas = SelectReplicas(snapshot, request.ActorId);
            var record = await ReadQuorumAsync(actorId, snapshot, replicas, cancellationToken)
                .ConfigureAwait(false);
            if (record is not null)
            {
                await RepairReplicasAsync(record, snapshot, replicas, cancellationToken)
                    .ConfigureAwait(false);
            }

            return new ActivationReply
            {
                Succeeded = true,
                Record = record is null ? null : ToDto(record)
            };
        }

        if (kind is AcquireKind or ReplicateAcquireKind)
        {
            if (kind == ReplicateAcquireKind)
            {
                var record = request.ToRecord();
                var applied = replica.ApplyReplica(record);
                var same = applied.OwnerReference == record.OwnerReference
                    && applied.ActivationId == record.ActivationId
                    && applied.Version == record.Version;
                return new ActivationReply
                {
                    Succeeded = same,
                    Changed = ReferenceEquals(applied, record),
                    Record = ToDto(applied)
                };
            }

            var snapshot = membership.Current;
            var replicas = SelectReplicas(snapshot, request.ActorId);
            var proposedOwner = request.ToOwner();
            if (!snapshot.TryGetMember(proposedOwner, out var proposedMember)
                || proposedMember!.State != ClusterMemberState.Ready)
            {
                return new ActivationReply
                {
                    Error = "The proposed actor owner is not an exact Ready member of the committed view."
                };
            }

            var existing = await ReadQuorumAsync(actorId, snapshot, replicas, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                await RepairReplicasAsync(existing, snapshot, replicas, cancellationToken)
                    .ConfigureAwait(false);

                // Membership removal is the fencing decision. Until the exact old
                // incarnation disappears from a committed view, sticky ownership wins.
                if (existing.OwnerReference is null
                    || snapshot.TryGetMember(existing.OwnerReference, out _)
                    || existing.ActivationId is not ActorActivationId existingActivation)
                {
                    return new ActivationReply
                    {
                        Succeeded = true,
                        Changed = false,
                        Record = ToDto(existing)
                    };
                }

                var superseded = await ExecuteLocalAsync(
                    ReleaseKind,
                    new ActivationRequest
                    {
                        ActorId = request.ActorId,
                        Activation = existingActivation.Value,
                        Version = existing.Version
                    },
                    cancellationToken).ConfigureAwait(false);
                if (!superseded.Succeeded || !superseded.Changed)
                {
                    return new ActivationReply
                    {
                        Error = superseded.Error
                            ?? "The fenced actor activation could not be released by a replica majority."
                    };
                }
            }

            var result = await replica.AcquireAsync(
                actorId,
                proposedOwner,
                new ActorActivationId(request.Activation),
                cancellationToken).ConfigureAwait(false);

            if (!result.Acquired)
            {
                return new ActivationReply
                {
                    Succeeded = true,
                    Changed = false,
                    Record = ToDto(result.Record)
                };
            }

            var acknowledgements = 1;
            request = ActivationRequest.ForRecord(result.Record);
            for (var i = 1; i < replicas.Count; i++)
            {
                try
                {
                    var reply = await SendRequestAsync(
                        replicas[i], snapshot.View, ReplicateAcquireKind, request, cancellationToken)
                        .ConfigureAwait(false);
                    if (reply.Succeeded)
                    {
                        acknowledgements++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                }
            }

            if (acknowledgements < replicas.Count / 2 + 1)
            {
                await replica.ReleaseAsync(
                    actorId,
                    new ActorActivationId(request.Activation),
                    result.Record.Version,
                    CancellationToken.None).ConfigureAwait(false);
                return new ActivationReply { Error = "Activation acquire lacked a replica majority." };
            }

            return new ActivationReply { Succeeded = true, Changed = true, Record = ToDto(result.Record) };
        }

        if (kind == ReplicateReleaseKind)
        {
            var changed = await replica.ReleaseAsync(
                actorId,
                new ActorActivationId(request.Activation),
                request.Version,
                cancellationToken).ConfigureAwait(false);
            return new ActivationReply { Succeeded = true, Changed = changed };
        }

        var current = await replica.ResolveAsync(actorId, cancellationToken).ConfigureAwait(false);
        if (current is null
            || current.ActivationId != new ActorActivationId(request.Activation)
            || current.Version != request.Version)
        {
            return new ActivationReply { Succeeded = true, Changed = false };
        }

        var releaseAcks = 1;
        var releaseSnapshot = membership.Current;
        var releaseReplicas = SelectReplicas(releaseSnapshot, request.ActorId);
        for (var i = 1; i < releaseReplicas.Count; i++)
        {
            try
            {
                var reply = await SendRequestAsync(
                    releaseReplicas[i], releaseSnapshot.View, ReplicateReleaseKind, request, cancellationToken)
                    .ConfigureAwait(false);
                if (reply.Succeeded)
                {
                    releaseAcks++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
            }
        }

        if (releaseAcks < releaseReplicas.Count / 2 + 1)
        {
            return new ActivationReply { Error = "Activation release lacked a replica majority." };
        }

        var released = await replica.ReleaseAsync(
            actorId,
            new ActorActivationId(request.Activation),
            request.Version,
            cancellationToken).ConfigureAwait(false);
        return new ActivationReply { Succeeded = true, Changed = released };
    }

    private async ValueTask<ActorDirectoryRecord?> ReadQuorumAsync(
        ActorId actorId,
        ClusterMembershipSnapshot snapshot,
        IReadOnlyList<ClusterMember> replicas,
        CancellationToken cancellationToken)
    {
        var replies = new List<ActorDirectoryRecord?>
        {
            await replica.ResolveAsync(actorId, cancellationToken).ConfigureAwait(false)
        };
        var request = new ActivationRequest { ActorId = actorId.Value };
        for (var i = 1; i < replicas.Count; i++)
        {
            try
            {
                var reply = await SendRequestAsync(
                    replicas[i], snapshot.View, ReplicaResolveKind, request, cancellationToken)
                    .ConfigureAwait(false);
                if (reply.Succeeded)
                {
                    replies.Add(reply.Record is null ? null : FromDto(reply.Record));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
            }
        }

        var quorum = replicas.Count / 2 + 1;
        var records = replies.Where(static record => record is not null).Cast<ActorDirectoryRecord>();
        foreach (var group in records.GroupBy(ActivationIdentity.FromRecord))
        {
            if (group.Count() >= quorum)
            {
                return group.OrderByDescending(static record => record.Version).First();
            }
        }

        if (replies.Count(static record => record is null) >= quorum)
        {
            return null;
        }

        throw new ActorDirectoryUnavailableException(
            "Activation read did not reach an agreeing replica majority.");
    }

    private async ValueTask RepairReplicasAsync(
        ActorDirectoryRecord record,
        ClusterMembershipSnapshot snapshot,
        IReadOnlyList<ClusterMember> replicas,
        CancellationToken cancellationToken)
    {
        replica.ApplyReplica(record);
        var request = ActivationRequest.ForRecord(record);
        for (var i = 1; i < replicas.Count; i++)
        {
            try
            {
                await SendRequestAsync(
                    replicas[i], snapshot.View, ReplicateAcquireKind, request, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
            }
        }
    }

    private async ValueTask<ActivationReply> SendRequestAsync(
        ClusterMember target,
        MembershipViewId view,
        string kind,
        ActivationRequest request,
        CancellationToken cancellationToken)
    {
        var correlation = Guid.NewGuid().ToString("N");
        var pending = gateway.RegisterPendingAsync(correlation, timeout, cancellationToken);
        var message = new ClusterMessage(
            Route,
            kind,
            JsonSerializer.SerializeToUtf8Bytes(request),
            DateTimeOffset.UtcNow.Add(timeout),
            localNode.NodeId,
            correlation,
            orderedBy: request.ActorId);
        var status = await exactSender.SendAsync(
            target.Reference,
            view,
            Route,
            message,
            cancellationToken).ConfigureAwait(false);
        if (status != ClusterSendStatus.Accepted)
        {
            gateway.TryCancelPending(correlation);
            throw new ActorDirectoryUnavailableException(
                $"Activation replica send failed with status '{status}'.");
        }

        var payload = await pending.ConfigureAwait(false);
        return JsonSerializer.Deserialize<ActivationReply>(payload.Span)
            ?? throw new ActorDirectoryUnavailableException("Activation replica returned no reply.");
    }

    private static IReadOnlyList<ClusterMember> SelectReplicas(
        ClusterMembershipSnapshot snapshot,
        string actorId)
    {
        var partition = (int)(Hash(actorId) % PartitionCount);
        return snapshot.Members
            .Where(static member => member.State == ClusterMemberState.Ready)
            .OrderByDescending(member => Hash(partition + "\0" + member.Reference.ToString()))
            .ThenBy(member => member.Reference.Node.Value, StringComparer.Ordinal)
            .Take(ReplicaCount)
            .ToArray();
    }

    private static ulong Hash(string value)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        var bytes = Encoding.UTF8.GetBytes(value);
        for (var i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= prime;
        }

        return hash;
    }

    private static ActivationRecordDto ToDto(ActorDirectoryRecord record) => new()
    {
        ActorId = record.ActorId.Value,
        Cluster = record.OwnerReference?.Cluster.Value ?? Guid.Empty,
        Node = record.Node.Value,
        Incarnation = record.OwnerReference?.Incarnation.Value ?? Guid.Empty,
        Activation = record.ActivationId?.Value ?? Guid.Empty,
        Version = record.Version,
        UpdatedAt = record.UpdatedAt
    };

    private static ActorDirectoryRecord FromDto(ActivationRecordDto dto) => new(
        ActorId.From(dto.ActorId),
        new NodeReference(
            new ClusterIncarnationId(dto.Cluster),
            new NodeId(dto.Node),
            new NodeIncarnationId(dto.Incarnation)),
        new ActorActivationId(dto.Activation),
        dto.Version,
        dto.UpdatedAt);

    private sealed class ActivationRequest
    {
        public string ActorId { get; set; } = string.Empty;
        public Guid Cluster { get; set; }
        public string Node { get; set; } = string.Empty;
        public Guid Incarnation { get; set; }
        public Guid Activation { get; set; }
        public long Version { get; set; }

        public static ActivationRequest ForAcquire(
            ActorId actorId,
            NodeReference owner,
            ActorActivationId activation) => new()
        {
            ActorId = actorId.Value,
            Cluster = owner.Cluster.Value,
            Node = owner.Node.Value,
            Incarnation = owner.Incarnation.Value,
            Activation = activation.Value
        };

        public static ActivationRequest ForRecord(ActorDirectoryRecord record) => new()
        {
            ActorId = record.ActorId.Value,
            Cluster = record.OwnerReference!.Cluster.Value,
            Node = record.OwnerReference.Node.Value,
            Incarnation = record.OwnerReference.Incarnation.Value,
            Activation = record.ActivationId!.Value.Value,
            Version = record.Version,
            UpdatedAt = record.UpdatedAt
        };

        public NodeReference ToOwner() => new(
            new ClusterIncarnationId(Cluster),
            new NodeId(Node),
            new NodeIncarnationId(Incarnation));

        public ActorDirectoryRecord ToRecord() => new(
            global::Lakona.Game.Server.Actors.ActorId.From(ActorId),
            ToOwner(),
            new ActorActivationId(Activation),
            Version,
            UpdatedAt);

        public DateTimeOffset UpdatedAt { get; set; }
    }

    private readonly record struct ActivationIdentity(
        NodeReference Owner,
        ActorActivationId Activation,
        long Version)
    {
        public static ActivationIdentity FromRecord(ActorDirectoryRecord record) => new(
            record.OwnerReference!,
            record.ActivationId!.Value,
            record.Version);
    }

    private sealed class ActivationReply
    {
        public bool Succeeded { get; set; }
        public bool Changed { get; set; }
        public ActivationRecordDto? Record { get; set; }
        public string? Error { get; set; }
    }

    private sealed class ActivationRecordDto
    {
        public string ActorId { get; set; } = string.Empty;
        public Guid Cluster { get; set; }
        public string Node { get; set; } = string.Empty;
        public Guid Incarnation { get; set; }
        public Guid Activation { get; set; }
        public long Version { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
