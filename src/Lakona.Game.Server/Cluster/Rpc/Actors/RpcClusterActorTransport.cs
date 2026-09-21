using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;

namespace Lakona.Game.Server.Actors;

internal sealed class RpcClusterActorTransport : IClusterActorTransport
{
    private readonly IClusterClientFactory clientFactory;
    private readonly IClusterMembership membership;
    private readonly TimeProvider timeProvider;

    public RpcClusterActorTransport(
        IClusterClientFactory clientFactory,
        IClusterMembership membership,
        TimeProvider? timeProvider = null)
    {
        this.clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        this.membership = membership ?? throw new ArgumentNullException(nameof(membership));
        this.timeProvider = timeProvider ?? TimeProvider.System;
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
        if (cancellationToken.IsCancellationRequested)
        {
            return RemoteActorInvocationResult.Failed(
                RemoteActorStatus.Cancelled,
                "Remote Actor invocation was cancelled by its caller.",
                RemoteActorRetrySafety.Indeterminate);
        }

        ClusterInvocationLifetime lifetime;
        try
        {
            lifetime = ClusterInvocationLifetime.FromDeadline(
                invocation.Deadline,
                timeProvider,
                cancellationToken);
        }
        catch (ArgumentOutOfRangeException)
        {
            return RemoteActorInvocationResult.Failed(
                RemoteActorStatus.Expired,
                "Remote Actor invocation deadline has expired.",
                RemoteActorRetrySafety.DefinitelyNotExecuted);
        }

        using (lifetime)
        {
            return await InvokeWithinLifetimeAsync(
                    invocation,
                    rpcMethodId,
                    lifetime,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask<RemoteActorInvocationResult> InvokeWithinLifetimeAsync(
        RemoteActorInvocation invocation,
        int rpcMethodId,
        ClusterInvocationLifetime lifetime,
        CancellationToken callerCancellationToken)
    {
        var resolution = ResolveTarget(invocation);
        if (lifetime.Token.IsCancellationRequested)
        {
            return lifetime.ToCancellationResult(
                callerCancellationToken,
                new OperationCanceledException(lifetime.Token));
        }

        if (resolution.Location is null)
        {
            return ToResult(resolution.Status);
        }

        var requestStarted = false;
        try
        {
            var client = await clientFactory
                .GetClientAsync(resolution.Location, lifetime.Token)
                .ConfigureAwait(false);
            if (client is not RpcClientRuntime runtime)
            {
                return RemoteActorInvocationResult.Failed(
                    RemoteActorStatus.HandlerUnavailable,
                    "The cluster RPC client does not support raw Actor calls.",
                    RemoteActorRetrySafety.DefinitelyNotExecuted);
            }

            using var response = await runtime.CallRawAsync(
                    ClusterProtocol.ServiceId,
                    rpcMethodId,
                    writer =>
                    {
                        var timeToLive = lifetime.Remaining;
                        if (timeToLive <= TimeSpan.Zero)
                        {
                            throw new OperationCanceledException(lifetime.Token);
                        }

                        requestStarted = true;
                        ClusterActorWireCodec.WriteRequest(
                            writer,
                            invocation,
                            resolution.Location,
                            timeToLive);
                    },
                    lifetime.Token)
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
        catch (OperationCanceledException exception)
        {
            return lifetime.ToCancellationResult(callerCancellationToken, exception);
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
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException)
        {
            return RemoteActorInvocationResult.Failed(
                RemoteActorStatus.NodeUnavailable,
                exception.Message,
                requestStarted
                    ? RemoteActorRetrySafety.Indeterminate
                    : RemoteActorRetrySafety.DefinitelyNotExecuted);
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
            || target.State != ClusterMemberState.Active)
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
