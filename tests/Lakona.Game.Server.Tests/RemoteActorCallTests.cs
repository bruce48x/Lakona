using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class RemoteActorCallTests
{
    [Theory]
    [InlineData(RemoteActorStatus.RouteNotFound, typeof(ActorNotFoundException), ActorCallStatus.ActorNotFound)]
    [InlineData(RemoteActorStatus.HandlerUnavailable, typeof(ActorNotFoundException), ActorCallStatus.ActorNotFound)]
    [InlineData(RemoteActorStatus.NodeUnavailable, typeof(NodeUnavailableException), ActorCallStatus.NodeUnavailable)]
    [InlineData(RemoteActorStatus.Timeout, typeof(ActorCallTimeoutException), ActorCallStatus.Timeout)]
    [InlineData(RemoteActorStatus.Backpressure, typeof(ActorBackpressureException), ActorCallStatus.Backpressure)]
    [InlineData(RemoteActorStatus.Expired, typeof(ActorCallException), ActorCallStatus.Expired)]
    [InlineData(RemoteActorStatus.SerializationFailed, typeof(ActorCallException), ActorCallStatus.Failed)]
    public void CreateException_maps_remote_status_to_actor_call_exception(
        RemoteActorStatus remoteStatus,
        Type exceptionType,
        ActorCallStatus actorStatus)
    {
        var actorId = ActorId.From("room/1001");
        var node = new NodeId("node-b");
        var result = RemoteActorInvocationResult.Failed(remoteStatus, "send failed");

        var exception = RemoteActorCall.CreateException(
            result,
            actorId,
            "room",
            "join",
            node,
            "corr-1");

        Assert.IsType(exceptionType, exception);
        Assert.Equal(actorStatus, exception.Status);
        Assert.Equal(actorId, exception.ActorId);
        Assert.Equal("room", exception.ActorName);
        Assert.Equal("join", exception.MethodName);
        Assert.Equal(node, exception.Node);
        Assert.Equal("corr-1", exception.CorrelationId);
        Assert.Contains("send failed", exception.Message);
    }

    [Fact]
    public void EnsureReplied_throws_operation_cancelled_for_cancelled_result()
    {
        var result = RemoteActorInvocationResult.Failed(RemoteActorStatus.Cancelled, "cancelled");

        Assert.Throws<OperationCanceledException>(() =>
            RemoteActorCall.EnsureReplied(
                result,
                ActorId.From("room/1001"),
                "room",
                "join",
                new NodeId("node-b"),
                "corr-1"));
    }

    [Fact]
    public void EnsureAccepted_allows_accepted_result()
    {
        RemoteActorCall.EnsureAccepted(
            RemoteActorInvocationResult.Accepted(),
            ActorId.From("room/1001"),
            "room",
            "leave",
            new NodeId("node-b"),
            "corr-1");
    }

    [Fact]
    public void EnsureReplied_allows_replied_result()
    {
        RemoteActorCall.EnsureReplied(
            RemoteActorInvocationResult.Replied(new byte[] { 1, 2, 3 }),
            ActorId.From("room/1001"),
            "room",
            "join",
            new NodeId("node-b"),
            "corr-1");
    }
}
