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

    private sealed class TestActor
    {
    }
}
