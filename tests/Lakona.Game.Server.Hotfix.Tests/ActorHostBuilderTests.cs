using Lakona.Game.Server.Hotfix.Abstractions;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class ActorHostBuilderTests
{
    [Fact]
    public void RegisterStartupRejectsBlankName()
    {
        var builder = new ActorHostBuilder();

        Assert.Throws<ArgumentException>(() =>
            builder.RegisterStartup(" ", static _ => ActorStartupPlan.Empty));
    }

    [Fact]
    public void RegisterStartupRejectsDuplicateName()
    {
        var builder = new ActorHostBuilder();
        builder.RegisterStartup("matchmaking", static _ => ActorStartupPlan.Empty);

        Assert.Throws<InvalidOperationException>(() =>
            builder.RegisterStartup("Matchmaking", static _ => ActorStartupPlan.Empty));
    }

    [Fact]
    public void RegisterPlacementRejectsDuplicateActor()
    {
        var builder = new ActorHostBuilder();
        builder.RegisterPlacement<TestActor, string>(static context => context.Candidates[0]);

        Assert.Throws<InvalidOperationException>(() =>
            builder.RegisterPlacement<TestActor, string>(static context => context.Candidates[0]));
    }

    [Fact]
    public void RegisterPlacementStoresActorAndKeyTypes()
    {
        var builder = new ActorHostBuilder();

        builder.RegisterPlacement<TestActor, string>(static context => context.Candidates[0]);

        var placement = Assert.Single(builder.Placements);
        Assert.Equal(typeof(TestActor), placement.ActorType);
        Assert.Equal(typeof(string), placement.KeyType);
    }

    [Fact]
    public void StartupsSnapshotCannotBypassValidation()
    {
        var builder = new ActorHostBuilder();
        builder.RegisterStartup("matchmaking", static _ => ActorStartupPlan.Empty);

        var snapshot = Assert.IsType<ActorStartupDeclaration[]>(builder.Startups);
        Array.Resize(
            ref snapshot,
            2);
        snapshot[1] = new ActorStartupDeclaration("Matchmaking", static _ => ActorStartupPlan.Empty);

        Assert.Throws<InvalidOperationException>(() =>
            builder.RegisterStartup("Matchmaking", static _ => ActorStartupPlan.Empty));
        Assert.Single(builder.Startups);
    }

    [Fact]
    public void ActorStartupPlanSnapshotsActors()
    {
        var actors = new List<ActorStartupInstance>
        {
            new(typeof(TestActor), "first"),
        };

        var plan = new ActorStartupPlan(actors);
        actors.Add(new ActorStartupInstance(typeof(TestActor), "second"));

        var actor = Assert.Single(plan.Actors);
        Assert.Equal("first", actor.ActorId);
    }

    [Fact]
    public void ActorHostCandidateRejectsBlankNodeId()
    {
        Assert.Throws<ArgumentException>(() => new ActorHostCandidate(" "));
    }

    [Fact]
    public void ActorHostCandidateSnapshotsMetadata()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["region"] = "east",
        };

        var candidate = new ActorHostCandidate("node-1", metadata);
        metadata["region"] = "west";

        Assert.Equal("east", candidate.Metadata["region"]);
    }

    private sealed class TestActor
    {
    }
}
