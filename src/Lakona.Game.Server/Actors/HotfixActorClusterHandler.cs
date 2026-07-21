using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Lakona.Game.Server.Hosting;

namespace Lakona.Game.Server.Actors;

public sealed class HotfixActorClusterHandler : IClusterMessageHandler
{
    private readonly IActorRuntime _runtime;
    private readonly IRemoteActorSerializer _serializer;
    private readonly IClusterNodeSender _nodeSender;
    private readonly LocalActorNodeIdentity _localNode;
    private readonly IServiceProvider _services;
    private readonly ILogger<HotfixActorClusterHandler>? _logger;

    public HotfixActorClusterHandler(
        IActorRuntime runtime,
        IRemoteActorSerializer serializer,
        IClusterNodeSender nodeSender,
        LocalActorNodeIdentity localNode,
        IServiceProvider services,
        ILogger<HotfixActorClusterHandler>? logger = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
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

    private async ValueTask<ClusterSendStatus> HandleCoreAsync(
        ClusterMessage message,
        CancellationToken cancellationToken)
    {

        if (string.Equals(message.Kind, ActorHostClient.MessageKind, StringComparison.Ordinal))
        {
            return await HandleActorHostCreateAsync(message, cancellationToken).ConfigureAwait(false);
        }

        if (!ClusterActorEnvelope.TryFromClusterMessage(message, out var envelope) ||
            envelope is null ||
            !string.Equals(envelope.Kind, HotfixActorApiMetadata.ActorMessageKind, StringComparison.Ordinal) ||
            !envelope.Metadata.TryGetValue(HotfixActorApiMetadata.MethodIdKey, out var methodIdText) ||
            !ulong.TryParse(methodIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var methodId))
        {
            return ClusterSendStatus.RouteNotFound;
        }

        var actorId = ActorId.From(envelope.ActorId);
        if (!await ValidateActivationAsync(actorId, envelope, cancellationToken).ConfigureAwait(false))
        {
            return ClusterSendStatus.StaleRoute;
        }

        var accessor = _services.GetService<IHotfixRuntimeAccessor>();
        if (accessor is null)
        {
            return ClusterSendStatus.RouteNotFound;
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

        var snapshot = lease.Snapshot;
        var table = snapshot.DispatchTable;
        if (table is null || !table.TryResolveActorMethod(methodId, out var descriptor))
        {
            _logger?.LogWarning("Remote actor method {MethodId} was not found for actor id {ActorId}.", methodId, envelope.ActorId);
            lease.Dispose();
            return ClusterSendStatus.RouteNotFound;
        }

        object? request;
        try
        {
            request = _serializer.Deserialize(envelope.Payload, descriptor.RequestType);
        }
        catch
        {
            lease.Dispose();
            return ClusterSendStatus.DeserializationFailed;
        }

        if (descriptor.ResultType is null)
        {
            if (envelope.ReplyCorrelationId is not null)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    lease.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var callState = new ActorDispatchState(
                    lease,
                    table,
                    descriptor.MethodKey,
                    request);
                try
                {
                    await _runtime.TellAsync(
                        descriptor.ActorType,
                        actorId,
                        async (actor, ct) =>
                        {
                            try
                            {
                                using var dispatchScope = callState.EnterDispatchScope();
                                await callState.Table.InvokeActorAsync(
                                    callState.MethodKey,
                                    actor,
                                    callState.Request,
                                    expectedResultType: null,
                                    ct).ConfigureAwait(false);
                            }
                            finally
                            {
                                callState.DisposeLease();
                            }
                        },
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    callState.DisposeLease();
                    throw;
                }
                catch (ActorCallException exception)
                {
                    callState.DisposeLease();
                    LogActorCallFailure(actorId, descriptor.ActorType, exception);
                    return MapCallException(exception);
                }
                catch
                {
                    callState.DisposeLease();
                    return ClusterSendStatus.Failed;
                }

                return await RemoteActorGateway.SendReplyAsync(
                    _nodeSender,
                    _localNode.NodeId,
                    envelope.SourceNode,
                    envelope.ReplyCorrelationId,
                    ReadOnlyMemory<byte>.Empty,
                    cancellationToken).ConfigureAwait(false);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                lease.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }

            var postState = new ActorDispatchState(
                lease,
                table,
                descriptor.MethodKey,
                request);
            ActorTellResult result;
            try
            {
                result = _runtime.TryTell(
                    descriptor.ActorType,
                    actorId,
                    async (actor, ct) =>
                    {
                        try
                        {
                            using var dispatchScope = postState.EnterDispatchScope();
                            await postState.Table.InvokeActorAsync(
                                postState.MethodKey,
                                actor,
                                postState.Request,
                                expectedResultType: null,
                                ct).ConfigureAwait(false);
                        }
                        finally
                        {
                            postState.DisposeLease();
                        }
                    },
                    CancellationToken.None);
            }
            catch
            {
                postState.DisposeLease();
                return ClusterSendStatus.Failed;
            }

            if (result != ActorTellResult.Accepted)
            {
                postState.DisposeLease();
                _logger?.LogWarning("Remote actor tell for {ActorType} at {ActorId} was rejected with {Result}.", descriptor.ActorType.FullName, actorId.Value, result);
            }

            return MapTellResult(result);
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
                            ct).ConfigureAwait(false);
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ActorCallException exception)
            {
                LogActorCallFailure(actorId, descriptor.ActorType, exception);
                return MapCallException(exception);
            }
            catch
            {
                return ClusterSendStatus.Failed;
            }

            if (envelope.ReplyCorrelationId is not null)
            {
                ReadOnlyMemory<byte> replyPayload;
                try
                {
                    replyPayload = _serializer.Serialize(result, descriptor.ResultType);
                }
                catch
                {
                    return ClusterSendStatus.SerializationFailed;
                }

                return await RemoteActorGateway.SendReplyAsync(
                    _nodeSender,
                    _localNode.NodeId,
                    envelope.SourceNode,
                    envelope.ReplyCorrelationId,
                    replyPayload,
                    cancellationToken).ConfigureAwait(false);
            }

            return ClusterSendStatus.Accepted;
        }
        finally
        {
            lease.Dispose();
        }
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

    private void LogActorCallFailure(ActorId actorId, Type actorType, ActorCallException exception)
    {
        _logger?.LogWarning(
            exception,
            "Remote actor call for {ActorType} at {ActorId} failed with {Status}.",
            actorType.FullName,
            actorId.Value,
            exception.Status);
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
            var actorType = ResolvePlacementActorType(lease.Snapshot, request.Actor);
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

    private static Type? ResolvePlacementActorType(
        HotfixRuntimeSnapshot snapshot,
        string actorName)
    {
        foreach (var placement in snapshot.ActorPlacements)
        {
            if (string.Equals(ActorNameResolver.Resolve(placement.ActorType), actorName, StringComparison.Ordinal))
            {
                return placement.ActorType;
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

    private static ClusterSendStatus MapTellResult(ActorTellResult result)
    {
        return result switch
        {
            ActorTellResult.ActorNotFound => ClusterSendStatus.RouteNotFound,
            ActorTellResult.MailboxFull => ClusterSendStatus.Backpressure,
            ActorTellResult.ActorUnavailable => ClusterSendStatus.HandlerUnavailable,
            _ => ClusterSendStatus.Accepted
        };
    }

    private static ClusterSendStatus MapCallException(ActorCallException exception)
    {
        return exception.Status switch
        {
            ActorCallStatus.ActorNotFound => ClusterSendStatus.RouteNotFound,
            ActorCallStatus.Backpressure => ClusterSendStatus.Backpressure,
            ActorCallStatus.Timeout => ClusterSendStatus.Timeout,
            ActorCallStatus.Expired => ClusterSendStatus.Expired,
            _ => ClusterSendStatus.Failed
        };
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
