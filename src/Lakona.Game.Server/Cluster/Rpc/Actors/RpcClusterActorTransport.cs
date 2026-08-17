using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;

namespace Lakona.Game.Server.Actors;

internal sealed class RpcClusterActorTransport : IClusterActorTransport
{
    private readonly IClusterClientFactory clientFactory;
    private readonly IClusterMembership membership;

    public RpcClusterActorTransport(
        IClusterClientFactory clientFactory,
        IClusterMembership membership)
    {
        this.clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        this.membership = membership ?? throw new ArgumentNullException(nameof(membership));
    }

    public ValueTask<RemoteActorInvocationResult> AskAsync(
        RemoteActorInvocation invocation,
        CancellationToken cancellationToken)
    {
        return InvokeAsync(invocation, ClusterProtocol.Methods.ActorAsk, cancellationToken);
    }

    public ValueTask<RemoteActorInvocationResult> TellAsync(
        RemoteActorInvocation invocation,
        CancellationToken cancellationToken)
    {
        return InvokeAsync(invocation, ClusterProtocol.Methods.ActorTell, cancellationToken);
    }

    private async ValueTask<RemoteActorInvocationResult> InvokeAsync(
        RemoteActorInvocation invocation,
        int rpcMethodId,
        CancellationToken cancellationToken)
    {
        var timeout = invocation.Deadline - DateTimeOffset.UtcNow;
        if (timeout <= TimeSpan.Zero)
        {
            return RemoteActorInvocationResult.Failed(
                RemoteActorStatus.Expired,
                "Remote Actor invocation deadline has expired.");
        }

        var resolution = ResolveTarget(invocation);
        if (resolution.Location is null)
        {
            return ToResult(resolution.Status);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            var client = await clientFactory
                .GetClientAsync(resolution.Location, timeoutSource.Token)
                .ConfigureAwait(false);
            if (client is not RpcClientRuntime rawClient)
            {
                return RemoteActorInvocationResult.Failed(
                    RemoteActorStatus.HandlerUnavailable,
                    "The cluster RPC client does not support raw Actor calls.",
                    RemoteActorRetrySafety.DefinitelyNotExecuted);
            }

            using var response = await rawClient.CallRawAsync(
                    ClusterProtocol.ServiceId,
                    rpcMethodId,
                    writer => ClusterActorWireCodec.WriteRequest(
                        writer,
                        invocation,
                        resolution.Location),
                    timeoutSource.Token)
                .ConfigureAwait(false);
            var reply = ClusterActorWireCodec.DecodeReply(response.Memory);
            return reply.Status == RemoteActorStatus.Replied
                ? RemoteActorInvocationResult.Replied(invocation.DeserializeReply(reply.Body))
                : reply.Status == RemoteActorStatus.Accepted
                    ? RemoteActorInvocationResult.Accepted()
                    : RemoteActorInvocationResult.Failed(
                        reply.Status,
                        reply.Message ?? "Remote Actor call failed.",
                        reply.RetrySafety);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            return RemoteActorInvocationResult.Failed(
                RemoteActorStatus.Cancelled,
                exception.Message);
        }
        catch (OperationCanceledException exception)
        {
            return RemoteActorInvocationResult.Failed(
                RemoteActorStatus.Timeout,
                exception.Message);
        }
        catch (TimeoutException exception)
        {
            return RemoteActorInvocationResult.Failed(
                RemoteActorStatus.Timeout,
                exception.Message);
        }
        catch (RpcException exception)
        {
            return RemoteActorInvocationResult.Failed(
                RemoteActorStatus.NodeUnavailable,
                exception.Message);
        }
    }

    private (RouteLocation? Location, RemoteActorStatus Status) ResolveTarget(
        RemoteActorInvocation invocation)
    {
        var route = ClusterActorRouteKeys.ForActor(invocation.ActorId.Value);
        var snapshot = membership.Current;
        if (invocation.OwnerReference is not { } owner
            || invocation.ActivationId is null
            || snapshot.Cluster != owner.Cluster
            || !snapshot.TryGetMember(owner, out var target)
            || target is null
            || target.State != ClusterMemberState.Ready)
            return (null, RemoteActorStatus.NodeUnavailable);
        return (new RouteLocation(route, target.Reference, snapshot.View, target.ClusterEndpoint), RemoteActorStatus.Replied);

    }

    private static RemoteActorInvocationResult ToResult(RemoteActorStatus status)
    {
        return RemoteActorInvocationResult.Failed(
            status,
            $"Remote Actor target resolution failed with status: {status}.",
            RemoteActorRetrySafety.DefinitelyNotExecuted);
    }
}
