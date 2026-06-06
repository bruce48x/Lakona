using Lakona.Game.Cluster;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class NodeDirectoryModelTests
{
    [Fact]
    public void ServiceDescriptorRequiresKind()
    {
        Assert.Throws<ArgumentException>(() => new NodeServiceDescriptor("", "gateway"));
    }

    [Fact]
    public void ServiceDescriptorDefaultsNameToKind()
    {
        var descriptor = new NodeServiceDescriptor("gateway");

        Assert.Equal("gateway", descriptor.Kind);
        Assert.Equal("gateway", descriptor.Name);
    }

    [Fact]
    public void RegistrationRequiresAtLeastOneService()
    {
        Assert.Throws<ArgumentException>(() => new NodeRegistration(
            "local",
            "node-a",
            new Dictionary<string, NodeEndpoint>
            {
                ["cluster"] = new NodeEndpoint("tcp://127.0.0.1:21000")
            },
            Array.Empty<NodeServiceDescriptor>(),
            DateTimeOffset.UtcNow.AddSeconds(30)));
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
            registration.Services,
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
                new NodeServiceDescriptor("gateway")
            },
            DateTimeOffset.UtcNow.AddSeconds(30));
    }
}
