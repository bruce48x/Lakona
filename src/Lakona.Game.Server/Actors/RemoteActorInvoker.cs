using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed class RemoteActorInvoker : IRemoteActorInvoker
{
    private readonly RemoteActorGateway _gateway;
    private readonly NodeId _localNode;
    private readonly IClusterNodeSender _nodeSender;
    private readonly RemoteActorOptions _options;

    public RemoteActorInvoker(
        RemoteActorGateway gateway,
        NodeId localNode,
        IClusterNodeSender nodeSender,
        RemoteActorOptions? options = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _localNode = localNode;
        _nodeSender = nodeSender ?? throw new ArgumentNullException(nameof(nodeSender));
        _options = options ?? new RemoteActorOptions();
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

        var route = ClusterActorRouteKeys.ForActor(invocation.ActorId.Value);

        return await _nodeSender.SendAsync(
            invocation.Node,
            route,
            CreateMessage(invocation, includeReply),
            cancellationToken).ConfigureAwait(false);
    }

    private ClusterMessage CreateMessage(
        RemoteActorInvocation invocation,
        bool includeReply)
    {
        var envelope = new ClusterActorEnvelope(
            ClusterActorRouteKeys.ForActor(invocation.ActorId.Value),
            invocation.ActorId.Value,
            invocation.MethodName,
            invocation.Payload,
            invocation.Deadline,
            _localNode,
            correlationId: invocation.CorrelationId,
            replyCorrelationId: includeReply ? invocation.CorrelationId : null,
            orderedBy: invocation.ActorId.Value);

        return envelope.ToClusterMessage();
    }

    private static RemoteActorInvocationResult ToResult(ClusterSendStatus status)
    {
        var remoteStatus = MapStatus(status);
        return remoteStatus switch
        {
            RemoteActorStatus.Accepted => RemoteActorInvocationResult.Accepted(),
            _ => RemoteActorInvocationResult.Failed(
                remoteStatus,
                $"Remote actor send failed with cluster status: {status}.")
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
            ClusterSendStatus.Failed => RemoteActorStatus.NodeUnavailable,
            ClusterSendStatus.StaleRoute => RemoteActorStatus.NodeUnavailable,
            ClusterSendStatus.NodeEpochMismatch => RemoteActorStatus.NodeUnavailable,
            _ => RemoteActorStatus.NodeUnavailable
        };
    }
}
