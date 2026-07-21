using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix;

namespace Lakona.Game.Server.Actors;

public sealed class RemoteActorInvoker : IRemoteActorInvoker
{
    private readonly RemoteActorGateway _gateway;
    private readonly NodeId _localNode;
    private readonly IClusterNodeSender _nodeSender;
    private readonly RemoteActorOptions _options;
    private readonly IActorDirectory? _directory;
    private readonly IActorDirectoryCache? _directoryCache;
    private readonly IClusterMembership? _membership;

    public RemoteActorInvoker(
        RemoteActorGateway gateway,
        NodeId localNode,
        IClusterNodeSender nodeSender,
        RemoteActorOptions? options = null,
        IActorDirectory? directory = null,
        IActorDirectoryCache? directoryCache = null,
        IClusterMembership? membership = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _localNode = localNode;
        _nodeSender = nodeSender ?? throw new ArgumentNullException(nameof(nodeSender));
        _options = options ?? new RemoteActorOptions();
        _directory = directory;
        _directoryCache = directoryCache;
        _membership = membership;
    }

    public async ValueTask<RemoteActorInvocationResult> AskAsync(
        RemoteActorInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var timeout = invocation.Deadline - DateTimeOffset.UtcNow;
        if (timeout <= TimeSpan.Zero)
        {
            return RemoteActorInvocationResult.Failed(
                RemoteActorStatus.Expired,
                "Remote actor invocation deadline has expired.");
        }

        Task<ReadOnlyMemory<byte>> pendingReply;
        pendingReply = _gateway.RegisterPendingAsync(
            invocation.CorrelationId,
            timeout,
            cancellationToken);

        ClusterSendStatus status;
        try
        {
            status = await SendToInvocationNodeAsync(
                invocation,
                includeReply: true,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            _gateway.TryCancelPending(invocation.CorrelationId);
            return RemoteActorInvocationResult.Failed(
                RemoteActorStatus.Cancelled,
                exception.Message);
        }
        catch
        {
            _gateway.TryCancelPending(invocation.CorrelationId);
            throw;
        }

        if (status != ClusterSendStatus.Accepted)
        {
            _gateway.TryCancelPending(invocation.CorrelationId);
            return ToResult(status);
        }

        try
        {
            var payload = await pendingReply.ConfigureAwait(false);
            return RemoteActorInvocationResult.Replied(payload);
        }
        catch (TimeoutException exception)
        {
            return RemoteActorInvocationResult.Failed(
                RemoteActorStatus.Timeout,
                exception.Message);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            return RemoteActorInvocationResult.Failed(
                RemoteActorStatus.Cancelled,
                exception.Message);
        }
    }

    public async ValueTask<RemoteActorInvocationResult> TellAsync(
        RemoteActorInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        try
        {
            var status = await SendToInvocationNodeAsync(
                invocation,
                includeReply: false,
                cancellationToken).ConfigureAwait(false);

            return ToResult(status);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            return RemoteActorInvocationResult.Failed(
                RemoteActorStatus.Cancelled,
                exception.Message);
        }
    }

    private async ValueTask<ClusterSendStatus> SendToInvocationNodeAsync(
        RemoteActorInvocation invocation,
        bool includeReply,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTimeOffset.UtcNow;
        if (invocation.Deadline <= now)
        {
            return ClusterSendStatus.Expired;
        }

        invocation = await AttachActivationAsync(invocation, cancellationToken).ConfigureAwait(false);
        var route = ClusterActorRouteKeys.ForActor(invocation.ActorId.Value);
        var message = CreateMessage(invocation, includeReply);

        if (invocation.OwnerReference is not null
            && _membership is not null
            && _nodeSender is IExactClusterNodeSender exactSender)
        {
            return await exactSender.SendAsync(
                invocation.OwnerReference,
                _membership.Current.View,
                route,
                message,
                cancellationToken).ConfigureAwait(false);
        }

        return await _nodeSender.SendAsync(
            invocation.Node,
            invocation.ExpectedNodeEpoch,
            route,
            message,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<RemoteActorInvocation> AttachActivationAsync(
        RemoteActorInvocation invocation,
        CancellationToken cancellationToken)
    {
        if (invocation.OwnerReference is not null
            && invocation.ActivationId is not null
            && invocation.ActivationVersion > 0)
        {
            return invocation;
        }

        if (_directory is null || _membership is null)
        {
            return invocation;
        }

        ActorDirectoryRecord? record = null;
        if (_directoryCache is null
            || !_directoryCache.TryGetRecord(invocation.ActorId, out record)
            || record is null
            || record.Node != invocation.Node)
        {
            record = await _directory.ResolveAsync(invocation.ActorId, cancellationToken)
                .ConfigureAwait(false);
            if (record is null || record.Node != invocation.Node)
            {
                _directoryCache?.Remove(invocation.ActorId);
                return invocation;
            }

            _directoryCache?.Set(record);
        }

        if (record.OwnerReference is null || record.ActivationId is null)
        {
            return invocation;
        }

        return new RemoteActorInvocation(
            invocation.Node,
            invocation.ActorId,
            invocation.ActorName,
            invocation.MethodName,
            invocation.Payload,
            invocation.Deadline,
            invocation.CorrelationId,
            invocation.Metadata,
            invocation.ExpectedNodeEpoch,
            record.OwnerReference,
            record.ActivationId,
            record.Version);
    }

    private ClusterMessage CreateMessage(
        RemoteActorInvocation invocation,
        bool includeReply)
    {
        var metadata = new Dictionary<string, string>(invocation.Metadata, StringComparer.Ordinal);
        if (invocation.OwnerReference is not null
            && invocation.ActivationId is ActorActivationId activation
            && invocation.ActivationVersion > 0)
        {
            metadata[ActorActivationMetadata.ClusterKey] =
                invocation.OwnerReference.Cluster.Value.ToString("D");
            metadata[ActorActivationMetadata.NodeIncarnationKey] =
                invocation.OwnerReference.Incarnation.Value.ToString("D");
            metadata[ActorActivationMetadata.ActivationKey] = activation.Value.ToString("D");
            metadata[ActorActivationMetadata.VersionKey] =
                invocation.ActivationVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var envelope = new ClusterActorEnvelope(
            ClusterActorRouteKeys.ForActor(invocation.ActorId.Value),
            invocation.ActorId.Value,
            HotfixActorApiMetadata.ActorMessageKind,
            invocation.Payload,
            invocation.Deadline,
            _localNode,
            correlationId: invocation.CorrelationId,
            replyCorrelationId: includeReply ? invocation.CorrelationId : null,
            orderedBy: invocation.ActorId.Value,
            metadata: metadata);

        return envelope.ToClusterMessage();
    }

    private static RemoteActorInvocationResult ToResult(ClusterSendStatus status)
    {
        var remoteStatus = MapStatus(status);
        var retrySafety = status is ClusterSendStatus.RouteNotFound
            or ClusterSendStatus.HandlerUnavailable
            or ClusterSendStatus.StaleRoute
            or ClusterSendStatus.NodeEpochMismatch
            ? RemoteActorRetrySafety.DefinitelyNotExecuted
            : RemoteActorRetrySafety.Indeterminate;
        return remoteStatus switch
        {
            RemoteActorStatus.Accepted => RemoteActorInvocationResult.Accepted(),
            _ => RemoteActorInvocationResult.Failed(
                remoteStatus,
                $"Remote actor send failed with cluster status: {status}.",
                retrySafety)
        };
    }

    private static RemoteActorStatus MapStatus(ClusterSendStatus status)
    {
        return status switch
        {
            ClusterSendStatus.Accepted => RemoteActorStatus.Accepted,
            ClusterSendStatus.Expired => RemoteActorStatus.Expired,
            ClusterSendStatus.RouteNotFound => RemoteActorStatus.RouteNotFound,
            ClusterSendStatus.Backpressure => RemoteActorStatus.Backpressure,
            ClusterSendStatus.HandlerUnavailable => RemoteActorStatus.HandlerUnavailable,
            ClusterSendStatus.Timeout => RemoteActorStatus.Timeout,
            ClusterSendStatus.SerializationFailed => RemoteActorStatus.SerializationFailed,
            ClusterSendStatus.DeserializationFailed => RemoteActorStatus.DeserializationFailed,
            ClusterSendStatus.Failed => RemoteActorStatus.NodeUnavailable,
            ClusterSendStatus.StaleRoute => RemoteActorStatus.NodeUnavailable,
            ClusterSendStatus.NodeEpochMismatch => RemoteActorStatus.NodeUnavailable,
            _ => RemoteActorStatus.NodeUnavailable
        };
    }
}
