using Lakona.Game.Cluster;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class NodeDirectoryModelTests
{
    [Fact]
    public void NodeActorHostDescriptorRejectsBlankActorName()
    {
        var exception = Assert.Throws<ArgumentException>(() => new NodeActorHostDescriptor(" ", "hash-a", "build-a"));

        Assert.Contains("Actor host name is required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NodeActorHostDescriptorRejectsBlankPolicyHash()
    {
        var exception = Assert.Throws<ArgumentException>(() => new NodeActorHostDescriptor("room", " ", "build-a"));

        Assert.Contains("Actor host policy hash is required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NodeActorHostDescriptorRejectsBlankBuildTag()
    {
        var exception = Assert.Throws<ArgumentException>(() => new NodeActorHostDescriptor("room", "hash-a", " "));

        Assert.Contains("Actor host build tag is required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NodeActorHostDescriptorCopiesMetadata()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["region"] = "us-east",
            ["tier"] = "battle"
        };

        var descriptor = new NodeActorHostDescriptor("room", "hash-a", "build-a", metadata);
        metadata["region"] = "changed";

        Assert.Equal("room", descriptor.Actor);
        Assert.Equal("hash-a", descriptor.PolicyHash);
        Assert.Equal("build-a", descriptor.BuildTag);
        Assert.Equal("us-east", descriptor.Metadata["region"]);
    }

    [Fact]
    public void NodeRegistrationAllowsActorHosts()
    {
        var registration = new NodeRegistration(
            "game",
            new NodeId("room-1"),
            new Dictionary<string, NodeEndpoint>
            {
                ["cluster"] = new NodeEndpoint("tcp://127.0.0.1:21002")
            },
            new[]
            {
                new NodeActorHostDescriptor("room", "policy-a", "build-a")
            },
            DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Equal("room", Assert.Single(registration.ActorHosts).Actor);
    }

    [Fact]
    public void NodeRegistrationAllowsNoActorHosts()
    {
        var registration = new NodeRegistration(
            "game",
            new NodeId("gateway-1"),
            new Dictionary<string, NodeEndpoint>
            {
                ["cluster"] = new NodeEndpoint("tcp://127.0.0.1:21002"),
                ["websocket"] = new NodeEndpoint("ws://127.0.0.1:20000/ws")
            },
            DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Empty(registration.ActorHosts);
    }

    [Fact]
    public void RecordRejectsNegativeEpoch()
    {
        var registration = TestRegistration();

        Assert.Throws<ArgumentOutOfRangeException>(() => new NodeRecord(
            registration.ClusterName,
            registration.NodeId,
            -1,
            registration.Endpoints,
            registration.ActorHosts,
            registration.Labels,
            NodeState.Ready,
            DateTimeOffset.UtcNow.AddSeconds(30),
            DateTimeOffset.UtcNow));
    }

    private static NodeRegistration TestRegistration()
    {
        return new NodeRegistration(
            "local",
            "node-a",
            new Dictionary<string, NodeEndpoint>
            {
                ["cluster"] = new NodeEndpoint("tcp://127.0.0.1:21000")
            },
            DateTimeOffset.UtcNow.AddSeconds(30),
            labels: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["role"] = "gateway"
            });
    }
}
