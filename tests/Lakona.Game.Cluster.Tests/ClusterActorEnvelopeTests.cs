using Lakona.Game.Cluster;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class ClusterActorEnvelopeTests
{
    [Fact]
    public void ActorRouteKeyDoesNotEncodeNodeOrEndpoint()
    {
        var route = ClusterActorRouteKeys.ForActor("room/42");

        Assert.Equal("actor:room/42", route.Value);
        Assert.True(ClusterActorRouteKeys.TryGetActorId(route, out var actorId));
        Assert.Equal("room/42", actorId);
        Assert.DoesNotContain("node", route.Value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("endpoint", route.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnvelopeRoundTripsThroughClusterMessageAsBytesAndMetadata()
    {
        var envelope = new ClusterActorEnvelope(
            ClusterActorRouteKeys.ForActor("room/42"),
            "room/42",
            "join",
            new byte[] { 1, 2, 3 },
            DateTimeOffset.UtcNow.AddSeconds(10),
            "node-a",
            correlationId: "corr-1",
            traceId: "trace-1",
            replyCorrelationId: "reply-1",
            orderedBy: "room/42",
            metadata: new Dictionary<string, string>
            {
                ["schema"] = "v1"
            });

        var message = envelope.ToClusterMessage();
        var parsed = ClusterActorEnvelope.TryFromClusterMessage(message, out var roundTrip);

        Assert.True(parsed);
        Assert.NotNull(roundTrip);
        Assert.Equal("room/42", roundTrip.ActorId);
        Assert.Equal("join", roundTrip.Kind);
        Assert.Equal(new byte[] { 1, 2, 3 }, roundTrip.Payload.ToArray());
        Assert.Equal("corr-1", roundTrip.CorrelationId);
        Assert.Equal("trace-1", roundTrip.TraceId);
        Assert.Equal("reply-1", roundTrip.ReplyCorrelationId);
        Assert.Equal("room/42", roundTrip.OrderedBy);
        Assert.Equal("v1", roundTrip.Metadata["schema"]);
    }
}
