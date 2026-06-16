using Lakona.Game.Cluster;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class NodeDirectoryModelTests
{
    [Fact]
    public void FeatureNameRejectsBlankValue()
    {
        var ex = Assert.Throws<ArgumentException>(() => new FeatureName(" "));

        Assert.Contains("Feature name is required", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NodeFeatureDescriptorCopiesMetadata()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["region"] = "cn-east",
            ["capacity"] = "small"
        };

        var descriptor = new NodeFeatureDescriptor("battle-runtime", metadata);
        metadata["region"] = "changed";

        Assert.Equal("battle-runtime", descriptor.Name);
        Assert.Equal("cn-east", descriptor.Metadata["region"]);
        Assert.Equal("small", descriptor.Metadata["capacity"]);
    }

    [Fact]
    public void NodeRegistrationAllowsNoApplicationFeatures()
    {
        var registration = new NodeRegistration(
            "game",
            new NodeId("gateway-1"),
            new Dictionary<string, NodeEndpoint>
            {
                ["cluster"] = new NodeEndpoint("tcp://127.0.0.1:21002"),
                ["websocket"] = new NodeEndpoint("ws://127.0.0.1:20000/ws")
            },
            Array.Empty<NodeFeatureDescriptor>(),
            DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Empty(registration.Features);
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
            registration.Features,
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
            new[]
            {
                new NodeFeatureDescriptor("gateway")
            },
            DateTimeOffset.UtcNow.AddSeconds(30));
    }
}
