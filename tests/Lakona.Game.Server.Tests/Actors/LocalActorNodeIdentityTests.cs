using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class LocalActorNodeIdentityTests
{
    [Fact]
    public void Observe_publishes_one_exact_process_identity()
    {
        var identity = new LocalActorNodeIdentity("node-a");
        var reference = Reference("node-a", "61000000-0000-0000-0000-000000000000");

        identity.Observe(reference);
        identity.Observe(reference);

        Assert.Equal(reference, identity.Reference);
    }

    [Fact]
    public void Observe_rejects_another_process_incarnation()
    {
        var identity = new LocalActorNodeIdentity("node-a");
        identity.Observe(Reference("node-a", "62000000-0000-0000-0000-000000000000"));

        Assert.Throws<InvalidOperationException>(() =>
            identity.Observe(Reference("node-a", "63000000-0000-0000-0000-000000000000")));
    }

    private static NodeReference Reference(string node, string incarnation) => new(
        new ClusterIncarnationId(Guid.Parse("60000000-0000-0000-0000-000000000000")),
        new NodeId(node),
        new NodeIncarnationId(Guid.Parse(incarnation)));
}
