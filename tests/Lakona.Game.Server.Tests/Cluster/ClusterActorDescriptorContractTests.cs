using Lakona.Game.Cluster;
using Xunit;

namespace Lakona.Game.Server.Tests.Cluster;

public sealed class ClusterActorDescriptorContractTests
{
    [Fact]
    public void Descriptors_reject_blank_required_fields_consistently()
    {
        Assert.Throws<ArgumentException>(() => new NodeActorHostDescriptor(
            "",
            "policy",
            "build"));
        Assert.Throws<ArgumentException>(() => new StartupActorDescriptor(
            "",
            "policy",
            "build"));
        Assert.Throws<ArgumentException>(() => new NodeActorHostDescriptor(
            "room",
            " ",
            "build"));
        Assert.Throws<ArgumentException>(() => new StartupActorDescriptor(
            "lobby",
            " ",
            "build"));
        Assert.Throws<ArgumentException>(() => new NodeActorHostDescriptor(
            "room",
            "policy",
            "\t"));
        Assert.Throws<ArgumentException>(() => new StartupActorDescriptor(
            "lobby",
            "policy",
            "\t"));
    }

    [Fact]
    public void Descriptor_metadata_is_copied_and_validated_consistently()
    {
        var metadata = new Dictionary<string, string> { ["zone"] = "east" };

        var actorHost = new NodeActorHostDescriptor("room", "policy", "build", metadata);
        var startupActor = new StartupActorDescriptor("lobby", "policy", "build", metadata);
        metadata["zone"] = "west";

        Assert.Equal("east", actorHost.Metadata["zone"]);
        Assert.Equal("east", startupActor.Metadata["zone"]);
        Assert.Throws<ArgumentException>(() => new NodeActorHostDescriptor(
            "room",
            "policy",
            "build",
            new Dictionary<string, string> { [""] = "value" }));
        Assert.Throws<ArgumentException>(() => new StartupActorDescriptor(
            "lobby",
            "policy",
            "build",
            new Dictionary<string, string> { [""] = "value" }));
        Assert.Throws<ArgumentException>(() => new NodeActorHostDescriptor(
            "room",
            "policy",
            "build",
            new Dictionary<string, string> { ["zone"] = null! }));
        Assert.Throws<ArgumentException>(() => new StartupActorDescriptor(
            "lobby",
            "policy",
            "build",
            new Dictionary<string, string> { ["zone"] = null! }));
    }

    [Fact]
    public void Cluster_member_sorts_and_copies_both_descriptor_collections()
    {
        var actorHosts = new List<NodeActorHostDescriptor>
        {
            new("zeta", "policy", "build"),
            new("alpha", "policy", "build")
        };
        var startupActors = new List<StartupActorDescriptor>
        {
            new("zeta", "policy", "build"),
            new("alpha", "policy", "build")
        };

        var member = CreateMember(actorHosts, startupActors);
        actorHosts.Clear();
        startupActors.Clear();

        Assert.Equal(["alpha", "zeta"], member.ActorHosts.Select(static descriptor => descriptor.Actor));
        Assert.Equal(["alpha", "zeta"], member.StartupActors.Select(static descriptor => descriptor.Actor));
    }

    [Fact]
    public void Cluster_member_rejects_duplicate_actor_names_in_both_descriptor_collections()
    {
        var actorHost = new NodeActorHostDescriptor("room", "policy", "build");
        var startupActor = new StartupActorDescriptor("lobby", "policy", "build");

        Assert.Throws<ArgumentException>(() => CreateMember(
            [actorHost, actorHost],
            []));
        Assert.Throws<ArgumentException>(() => CreateMember(
            [],
            [startupActor, startupActor]));
    }

    [Fact]
    public void Cluster_member_applies_the_same_descriptor_limit_to_both_collections()
    {
        var actorHosts = Enumerable.Range(0, 257)
            .Select(static index => new NodeActorHostDescriptor(
                $"actor-{index}",
                "policy",
                "build"))
            .ToArray();
        var startupActors = Enumerable.Range(0, 257)
            .Select(static index => new StartupActorDescriptor(
                $"actor-{index}",
                "policy",
                "build"))
            .ToArray();

        Assert.Throws<ArgumentException>(() => CreateMember(actorHosts, []));
        Assert.Throws<ArgumentException>(() => CreateMember([], startupActors));
    }

    [Fact]
    public void Cluster_member_normalizes_null_collections_and_rejects_null_elements()
    {
        var member = CreateMember(null, null);

        Assert.Empty(member.ActorHosts);
        Assert.Empty(member.StartupActors);
        Assert.Throws<ArgumentException>(() => CreateMember(
            [null!],
            []));
        Assert.Throws<ArgumentException>(() => CreateMember(
            [],
            [null!]));
    }

    private static ClusterMember CreateMember(
        IReadOnlyList<NodeActorHostDescriptor>? actorHosts,
        IReadOnlyList<StartupActorDescriptor>? startupActors)
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("62000000-0000-0000-0000-000000000000"));
        var reference = new NodeReference(
            cluster,
            new NodeId("node-1"),
            new NodeIncarnationId(
                Guid.Parse("62000001-0000-0000-0000-000000000000")));
        return new ClusterMember(
            reference,
            ClusterMemberState.Active,
            new NodeEndpoint("tcp://127.0.0.1:24001"),
            labels: null,
            actorHosts,
            startupActors);
    }
}
