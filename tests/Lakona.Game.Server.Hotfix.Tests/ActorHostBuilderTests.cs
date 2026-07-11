using Lakona.Game.Server.Hotfix.Abstractions;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class ActorHostBuilderTests
{
    [Fact]
    public void RegisterStartupStoresActorKeyAndTypedSelector()
    {
        var builder = new ActorHostBuilder();
        StartupActorCandidate? selected = null;

        builder.RegisterStartup<TestActor, TenantKey>(context =>
        {
            Assert.Equal(new TenantKey("tenant-a"), context.Key);
            selected = context.Candidates[0];
            return selected;
        });

        var declaration = Assert.Single(builder.Startups);
        Assert.Equal(typeof(TestActor), declaration.ActorType);
        Assert.Equal(typeof(TenantKey), declaration.KeyType);

        var candidate = new StartupActorCandidate("node-1", 7);
        var selector = Assert.IsType<Func<StartupActorSelectionContext<TenantKey>, StartupActorCandidate>>(
            declaration.Selector);
        var result = selector(new StartupActorSelectionContext<TenantKey>([candidate], new TenantKey("tenant-a")));

        Assert.Same(candidate, result);
        Assert.Same(candidate, selected);
    }

    [Fact]
    public void RegisterStartupRejectsDuplicateActorWithDifferentKeyType()
    {
        var builder = new ActorHostBuilder();
        builder.RegisterStartup<TestActor, TenantKey>(static context => context.Candidates[0]);

        Assert.Throws<InvalidOperationException>(() =>
            builder.RegisterStartup<TestActor, string>(static context => context.Candidates[0]));
    }

    [Fact]
    public void RegisterStartupRejectsNullSelector()
    {
        var builder = new ActorHostBuilder();

        Assert.Throws<ArgumentNullException>(() =>
            builder.RegisterStartup<TestActor, TenantKey>(null!));
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
        builder.RegisterStartup<TestActor, TenantKey>(static context => context.Candidates[0]);

        var snapshot = Assert.IsType<ActorStartupDeclaration[]>(builder.Startups);
        Array.Resize(ref snapshot, 2);
        snapshot[1] = ActorStartupDeclaration.Create<TestActor, string>(
            static context => context.Candidates[0]);

        Assert.Throws<InvalidOperationException>(() =>
            builder.RegisterStartup<TestActor, string>(static context => context.Candidates[0]));
        Assert.Single(builder.Startups);
    }

    [Fact]
    public void StartupActorCandidateRejectsBlankNodeId()
    {
        Assert.Throws<ArgumentException>(() => new StartupActorCandidate(" ", 1));
    }

    [Fact]
    public void StartupActorCandidateRejectsNegativeNodeEpoch()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StartupActorCandidate("node-1", -1));
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

    [Fact]
    public void StartupActorCandidateSnapshotsMetadataWithOrdinalKeys()
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Region"] = "east",
        };

        var candidate = new StartupActorCandidate("node-1", 7, metadata);
        metadata["Region"] = "west";

        Assert.Equal("east", candidate.Metadata["Region"]);
        Assert.False(candidate.Metadata.ContainsKey("region"));
    }

    private sealed class TestActor
    {
    }

    private sealed record TenantKey(string Value);
}
