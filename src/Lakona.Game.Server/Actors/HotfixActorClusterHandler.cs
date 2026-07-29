using System.Buffers;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Core;

namespace Lakona.Game.Server.Actors;

public sealed class HotfixActorClusterHandler : IClusterMessageHandler
{
    private readonly IActorRuntime _runtime;
    private readonly IClusterNodeSender _nodeSender;
    private readonly LocalActorNodeIdentity _localNode;
    private readonly IServiceProvider _services;
    private readonly ILogger<HotfixActorClusterHandler>? _logger;

    public HotfixActorClusterHandler(
        IActorRuntime runtime,
        IClusterNodeSender nodeSender,
        LocalActorNodeIdentity localNode,
        IServiceProvider services,
        ILogger<HotfixActorClusterHandler>? logger = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _nodeSender = nodeSender ?? throw new ArgumentNullException(nameof(nodeSender));
        _localNode = localNode ?? throw new ArgumentNullException(nameof(localNode));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger;
    }

    public async ValueTask<ClusterSendStatus> HandleAsync(
        ClusterMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var admissionGate = _services.GetService<IDistributedWorkAdmissionGate>();
        DistributedWorkAdmission admission = default;
        if (admissionGate is not null && !admissionGate.TryEnter(out admission))
        {
            return ClusterSendStatus.Rejected;
        }

        try
        {
            return await HandleCoreAsync(message, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (admission.IsAdmitted)
            {
                admissionGate!.Exit(admission);
            }
        }
    }

    internal async ValueTask<TransportFrame> HandleActorRpcAsync(
        ReadOnlyMemory<byte> payload,
        bool tell,
        CancellationToken cancellationToken)
    {
        using var writer = new PooledFrameBufferWriter();
        await HandleActorRpcAsync(payload, tell, writer, cancellationToken)
            .ConfigureAwait(false);
        return writer.DetachFrame();
    }

    internal async ValueTask HandleActorRpcAsync(
        ReadOnlyMemory<byte> payload,
        bool tell,
        IBufferWriter<byte> response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        ClusterActorWireRequest wireRequest;
        try
        {
            wireRequest = ClusterActorWireCodec.DecodeRequest(payload);
        }
        catch
        {
            ClusterActorWireCodec.WriteReply(
                response,
                RemoteActorStatus.DeserializationFailed,
                "Remote Actor request header could not be decoded.",
                RemoteActorRetrySafety.DefinitelyNotExecuted);
            return;
        }

        var admissionGate = _services.GetService<IDistributedWorkAdmissionGate>();
        DistributedWorkAdmission admission = default;
        if (admissionGate is not null && !admissionGate.TryEnter(out admission))
        {
            ClusterActorWireCodec.WriteReply(
                response,
                RemoteActorStatus.Backpressure,
                "Distributed work admission is closed.",
                RemoteActorRetrySafety.DefinitelyNotExecuted);
            return;
        }

        try
        {
            await HandleActorRpcCoreAsync(wireRequest, tell, response, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (admission.IsAdmitted)
            {
                admissionGate!.Exit(admission);
            }
        }
    }

    private async ValueTask HandleActorRpcCoreAsync(
        ClusterActorWireRequest wireRequest,
        bool tell,
        IBufferWriter<byte> response,
        CancellationToken cancellationToken)
    {
        var header = wireRequest.Header;
        if (header.Deadline <= DateTimeOffset.UtcNow)
        {
            ClusterActorWireCodec.WriteReply(
                response,
                RemoteActorStatus.Expired,
                "Remote Actor invocation deadline has expired.",
                RemoteActorRetrySafety.DefinitelyNotExecuted);
            return;
        }

        var actorId = ActorId.From(header.ActorId);
        if (!await ValidateRpcTargetAndActivationAsync(actorId, header, cancellationToken)
                .ConfigureAwait(false))
        {
            ClusterActorWireCodec.WriteReply(
                response,
                RemoteActorStatus.NodeUnavailable,
                "Remote Actor target or activation is stale.",
                RemoteActorRetrySafety.DefinitelyNotExecuted);
            return;
        }

        var accessor = _services.GetService<IHotfixRuntimeAccessor>();
        if (accessor is null)
        {
            ClusterActorWireCodec.WriteReply(
                response,
                RemoteActorStatus.RouteNotFound,
                "The Hotfix runtime is unavailable.",
                RemoteActorRetrySafety.DefinitelyNotExecuted);
            return;
        }

        HotfixRuntimeSnapshotLease lease;
        try
        {
            lease = accessor.AcquireCurrent();
        }
        catch (ObjectDisposedException)
        {
            ClusterActorWireCodec.WriteReply(
                response,
                RemoteActorStatus.RouteNotFound,
                "The Hotfix runtime has stopped.",
                RemoteActorRetrySafety.DefinitelyNotExecuted);
            return;
        }

        var table = lease.Snapshot.DispatchTable;
        if (table is null || !table.TryResolveActorMethod(header.MethodId, out var descriptor))
        {
            lease.Dispose();
            ClusterActorWireCodec.WriteReply(
                response,
                RemoteActorStatus.RouteNotFound,
                $"Remote Actor method '{header.MethodId}' is not loaded.",
                RemoteActorRetrySafety.DefinitelyNotExecuted);
            return;
        }

        object? request;
        try
        {
            request = descriptor.Codec.DeserializeRequest(wireRequest.Body);
        }
        catch
        {
            lease.Dispose();
            ClusterActorWireCodec.WriteReply(
                response,
                RemoteActorStatus.DeserializationFailed,
                "Remote Actor request payload could not be decoded.",
                RemoteActorRetrySafety.DefinitelyNotExecuted);
            return;
        }

        if (tell)
        {
            if (descriptor.ResultType is not null)
            {
                lease.Dispose();
                ClusterActorWireCodec.WriteReply(
                    response,
                    RemoteActorStatus.HandlerUnavailable,
                    "A result-returning Actor method cannot be sent as a tell.",
                    RemoteActorRetrySafety.DefinitelyNotExecuted);
                return;
            }

            var state = new ActorDispatchState(lease, table, descriptor.MethodKey, request);
            ActorTellResult accepted;
            try
            {
                accepted = _runtime.TryTell(
                    descriptor.ActorType,
                    actorId,
                    async (actor, ct) =>
                    {
                        try
                        {
                            using var dispatchScope = state.EnterDispatchScope();
                            await state.Table.InvokeActorAsync(
                                    state.MethodKey,
                                    actor,
                                    state.Request,
                                    expectedResultType: null,
                                    ct)
                                .ConfigureAwait(false);
                        }
                        finally
                        {
                            state.DisposeLease();
                        }
                    },
                    CancellationToken.None);
            }
            catch
            {
                state.DisposeLease();
                ClusterActorWireCodec.WriteReply(response, RemoteActorStatus.NodeUnavailable);
                return;
            }

            if (accepted != ActorTellResult.Accepted)
            {
                state.DisposeLease();
            }

            ClusterActorWireCodec.WriteReply(response, MapTellStatus(accepted));
            return;
        }

        try
        {
            object? result;
            try
            {
                result = await _runtime.AskAsync(
                        descriptor.ActorType,
                        actorId,
                        async (actor, ct) =>
                        {
                            using var dispatchScope = lease.EnterDispatchScope();
                            return await table.InvokeActorAsync(
                                    descriptor.MethodKey,
                                    actor,
                                    request,
                                    descriptor.ResultType,
                                    ct)
                                .ConfigureAwait(false);
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ActorCallException exception)
            {
                ClusterActorWireCodec.WriteReply(
                    response,
                    MapRemoteStatus(exception),
                    exception.Message,
                    exception is ActorNotFoundException { DefinitelyNotExecuted: true }
                        ? RemoteActorRetrySafety.DefinitelyNotExecuted
                        : RemoteActorRetrySafety.Indeterminate);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                ClusterActorWireCodec.WriteReply(
                    response,
                    RemoteActorStatus.NodeUnavailable,
                    exception.Message);
                return;
            }

            ClusterActorWireCodec.WriteReply(
                response,
                RemoteActorStatus.Replied,
                writeBody: writer => descriptor.Codec.SerializeResult(writer, result));
        }
        finally
        {
            lease.Dispose();
        }
    }

    private async ValueTask<bool> ValidateRpcTargetAndActivationAsync(
        ActorId actorId,
        ClusterActorWireRequestHeader header,
        CancellationToken cancellationToken)
    {
        var membership = _services.GetService<IClusterMembership>();
        if (membership is null)
        {
            return true;
        }

        var snapshot = membership.Current;
        var localMember = snapshot.Members.SingleOrDefault(member =>
            member.Reference.Node == _localNode.NodeId
            && member.State == ClusterMemberState.Ready);
        if (localMember is null
            || header.TargetClusterIncarnation != snapshot.Cluster.Value
            || !string.Equals(header.TargetNode, localMember.Reference.Node.Value, StringComparison.Ordinal)
            || header.TargetNodeIncarnation != localMember.Reference.Incarnation.Value
            || header.TargetMembershipView is not long targetView
            || targetView <= 0
            || snapshot.View.Value < targetView
            || header.ActivationId is not Guid activationValue
            || activationValue == Guid.Empty
            || header.ActivationVersion <= 0)
        {
            return false;
        }

        var cache = _services.GetService<IActorDirectoryCache>();
        ActorDirectoryRecord? record = null;
        if (cache is null || !cache.TryGetRecord(actorId, out record) || record is null)
        {
            var directory = _services.GetService<IActorDirectory>();
            if (directory is null)
            {
                return false;
            }

            record = await directory.ResolveAsync(actorId, cancellationToken).ConfigureAwait(false);
            if (record is not null)
            {
                cache?.Set(record);
            }
        }

        return record?.OwnerReference == localMember.Reference
            && record.ActivationId == new ActorActivationId(activationValue)
            && record.Version == header.ActivationVersion;
    }

    private static RemoteActorStatus MapTellStatus(ActorTellResult result)
    {
        return result switch
        {
            ActorTellResult.Accepted => RemoteActorStatus.Accepted,
            ActorTellResult.ActorNotFound => RemoteActorStatus.RouteNotFound,
            ActorTellResult.MailboxFull => RemoteActorStatus.Backpressure,
            ActorTellResult.ActorUnavailable => RemoteActorStatus.HandlerUnavailable,
            _ => RemoteActorStatus.NodeUnavailable
        };
    }

    private static RemoteActorStatus MapRemoteStatus(ActorCallException exception)
    {
        return exception.Status switch
        {
            ActorCallStatus.ActorNotFound => RemoteActorStatus.RouteNotFound,
            ActorCallStatus.Backpressure => RemoteActorStatus.Backpressure,
            ActorCallStatus.Timeout => RemoteActorStatus.Timeout,
            ActorCallStatus.Expired => RemoteActorStatus.Expired,
            _ => RemoteActorStatus.NodeUnavailable
        };
    }

    private async ValueTask<ClusterSendStatus> HandleCoreAsync(
        ClusterMessage message,
        CancellationToken cancellationToken)
    {
        if (string.Equals(message.Kind, ActorHostClient.MessageKind, StringComparison.Ordinal))
        {
            return await HandleActorHostCreateAsync(message, cancellationToken).ConfigureAwait(false);
        }

        return ClusterSendStatus.RouteNotFound;
    }

    private async ValueTask<bool> ValidateActivationAsync(
        ActorId actorId,
        ClusterActorEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var membership = _services.GetService<IClusterMembership>();
        if (membership is null)
        {
            return true;
        }

        var snapshot = membership.Current;
        var localMember = snapshot.Members.SingleOrDefault(member =>
            member.Reference.Node == _localNode.NodeId
            && member.State == ClusterMemberState.Ready);
        if (localMember is null)
        {
            _logger?.LogWarning(
                "Actor activation for {ActorId} was fenced because local node {NodeId} is not Ready in membership view {View}.",
                actorId.Value,
                _localNode.NodeId.Value,
                snapshot.View.Value);
            return false;
        }

        if (!TryReadActivation(envelope.Metadata, out var cluster, out var nodeIncarnation,
                out var activation, out var version))
        {
            _logger?.LogWarning(
                "Actor activation for {ActorId} was fenced because exact activation metadata is missing or invalid.",
                actorId.Value);
            return false;
        }

        if (cluster != snapshot.Cluster
            || nodeIncarnation != localMember.Reference.Incarnation)
        {
            _logger?.LogWarning(
                "Actor activation for {ActorId} was fenced because its cluster or node incarnation does not match {LocalReference} in view {View}.",
                actorId.Value,
                localMember.Reference,
                snapshot.View.Value);
            return false;
        }

        var cache = _services.GetService<IActorDirectoryCache>();
        ActorDirectoryRecord? record = null;
        if (cache is null || !cache.TryGetRecord(actorId, out record) || record is null)
        {
            var directory = _services.GetService<IActorDirectory>();
            if (directory is null)
            {
                return false;
            }

            record = await directory.ResolveAsync(actorId, cancellationToken).ConfigureAwait(false);
            if (record is not null)
            {
                cache?.Set(record);
            }
        }

        var accepted = record?.OwnerReference == localMember.Reference
            && record.ActivationId == activation
            && record.Version == version;
        if (!accepted)
        {
            _logger?.LogWarning(
                "Actor activation for {ActorId} was fenced because directory owner/token/version does not match the invocation in view {View}.",
                actorId.Value,
                snapshot.View.Value);
        }

        return accepted;
    }

    private static bool TryReadActivation(
        IReadOnlyDictionary<string, string> metadata,
        out ClusterIncarnationId cluster,
        out NodeIncarnationId nodeIncarnation,
        out ActorActivationId activation,
        out long version)
    {
        cluster = default;
        nodeIncarnation = default;
        activation = default;
        version = 0;
        if (!metadata.TryGetValue(ActorActivationMetadata.ClusterKey, out var clusterText)
            || !metadata.TryGetValue(ActorActivationMetadata.NodeIncarnationKey, out var nodeText)
            || !metadata.TryGetValue(ActorActivationMetadata.ActivationKey, out var activationText)
            || !metadata.TryGetValue(ActorActivationMetadata.VersionKey, out var versionText)
            || !Guid.TryParse(clusterText, out var clusterValue)
            || !Guid.TryParse(nodeText, out var nodeValue)
            || !Guid.TryParse(activationText, out var activationValue)
            || !long.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out version)
            || clusterValue == Guid.Empty
            || nodeValue == Guid.Empty
            || activationValue == Guid.Empty
            || version <= 0)
        {
            return false;
        }

        cluster = new ClusterIncarnationId(clusterValue);
        nodeIncarnation = new NodeIncarnationId(nodeValue);
        activation = new ActorActivationId(activationValue);
        return true;
    }

    private async ValueTask<ClusterSendStatus> HandleActorHostCreateAsync(
        ClusterMessage message,
        CancellationToken cancellationToken)
    {
        var hosting = _services.GetService<ActorHosting>();
        var accessor = _services.GetService<IHotfixRuntimeAccessor>();
        if (hosting is null || accessor is null || message.CorrelationId is null)
        {
            return ClusterSendStatus.RouteNotFound;
        }

        ActorHostCreateRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<ActorHostCreateRequest>(message.Payload.Span);
        }
        catch
        {
            return ClusterSendStatus.DeserializationFailed;
        }

        if (request is null)
        {
            return ClusterSendStatus.DeserializationFailed;
        }

        HotfixRuntimeSnapshotLease lease;
        try
        {
            lease = accessor.AcquireCurrent();
        }
        catch (ObjectDisposedException)
        {
            return ClusterSendStatus.RouteNotFound;
        }

        ActorHostCreateReply reply;
        try
        {
            var actorType = ResolveActorType(lease.Snapshot, request.Actor);
            if (actorType is null)
            {
                return ClusterSendStatus.RouteNotFound;
            }

            var actorId = ActorId.From(request.ActorId);
            if (!await ValidateHostActivationAsync(actorId, request, cancellationToken)
                .ConfigureAwait(false))
            {
                return ClusterSendStatus.StaleRoute;
            }

            try
            {
                await InvokeHostingAsync(hosting, actorType, actorId, request.Mode, cancellationToken)
                    .ConfigureAwait(false);
                reply = new ActorHostCreateReply(true, _localNode.NodeId.Value, "created");
            }
            catch (ActorHostedElsewhereException ex)
            {
                reply = new ActorHostCreateReply(false, ex.OwnerNode.Value, ex.Message);
            }
            catch (Exception ex)
            {
                reply = new ActorHostCreateReply(false, null, ex.Message);
            }
        }
        finally
        {
            lease.Dispose();
        }

        return await RemoteActorGateway.SendReplyAsync(
            _nodeSender,
            _localNode.NodeId,
            message.SourceNode,
            message.CorrelationId,
            JsonSerializer.SerializeToUtf8Bytes(reply),
            cancellationToken).ConfigureAwait(false);
    }

    private ValueTask<bool> ValidateHostActivationAsync(
        ActorId actorId,
        ActorHostCreateRequest request,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (request.ClusterIncarnation is not null)
        {
            metadata[ActorActivationMetadata.ClusterKey] = request.ClusterIncarnation;
        }

        if (request.NodeIncarnation is not null)
        {
            metadata[ActorActivationMetadata.NodeIncarnationKey] = request.NodeIncarnation;
        }

        if (request.ActivationId is not null)
        {
            metadata[ActorActivationMetadata.ActivationKey] = request.ActivationId;
        }

        if (request.ActivationVersion > 0)
        {
            metadata[ActorActivationMetadata.VersionKey] = request.ActivationVersion.ToString(
                CultureInfo.InvariantCulture);
        }

        return ValidateActivationAsync(
            actorId,
            new ClusterActorEnvelope(
                ClusterActorRouteKeys.ForActor(actorId.Value),
                actorId.Value,
                HotfixActorApiMetadata.ActorMessageKind,
                ReadOnlyMemory<byte>.Empty,
                DateTimeOffset.MaxValue,
                _localNode.NodeId,
                metadata: metadata),
            cancellationToken);
    }

    private static Type? ResolveActorType(
        HotfixRuntimeSnapshot snapshot,
        string actorName)
    {
        foreach (var actorType in snapshot.ActorTypes)
        {
            if (string.Equals(ActorNameResolver.Resolve(actorType), actorName, StringComparison.Ordinal))
            {
                return actorType;
            }
        }

        return null;
    }

    private static async ValueTask InvokeHostingAsync(
        ActorHosting hosting,
        Type actorType,
        ActorId actorId,
        string mode,
        CancellationToken cancellationToken)
    {
        var methodName = string.Equals(mode, "create", StringComparison.OrdinalIgnoreCase)
            ? nameof(ActorHosting.CreateAsync)
            : string.Equals(mode, "ensure", StringComparison.OrdinalIgnoreCase)
                ? nameof(ActorHosting.EnsureAsync)
                : throw new InvalidOperationException($"Unknown actor host create mode '{mode}'.");
        var method = typeof(ActorHosting)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(candidate => candidate.Name == methodName && candidate.IsGenericMethodDefinition);
        var task = (ValueTask)method
            .MakeGenericMethod(actorType)
            .Invoke(hosting, [actorId, cancellationToken])!;
        await task.ConfigureAwait(false);
    }

    private sealed class ActorDispatchState(
        HotfixRuntimeSnapshotLease lease,
        HotfixDispatchTable table,
        string methodKey,
        object? request)
    {
        private int _leaseDisposed;

        public HotfixDispatchTable Table { get; } = table;

        public string MethodKey { get; } = methodKey;

        public object? Request { get; } = request;

        public IDisposable EnterDispatchScope()
        {
            return lease.EnterDispatchScope();
        }

        public void DisposeLease()
        {
            if (Interlocked.Exchange(ref _leaseDisposed, 1) == 0)
            {
                lease.Dispose();
            }
        }
    }
}
