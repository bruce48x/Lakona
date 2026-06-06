using Lakona.Game.Cluster;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class InMemoryRouteDirectoryTests
{
    [Fact]
    public async Task RegisterReplacesExistingRouteLocation()
    {
        var directory = new InMemoryRouteDirectory();
        var now = DateTimeOffset.UtcNow;
        var route = new RouteKey("room/1");

        var first = await directory.RegisterAsync(
            new RouteLocation(route, "node-a", new NodeEndpoint("in-memory://node-a"), now.AddMinutes(1), generation: 1),
            TestContext.Current.CancellationToken);
        var second = await directory.RegisterAsync(
            new RouteLocation(route, "node-b", new NodeEndpoint("in-memory://node-b"), now.AddMinutes(1), generation: 2),
            TestContext.Current.CancellationToken);

        var resolved = await directory.ResolveAsync(route, now, TestContext.Current.CancellationToken);

        Assert.Equal(RouteRegistrationStatus.Registered, first);
        Assert.Equal(RouteRegistrationStatus.Registered, second);
        Assert.NotNull(resolved);
        Assert.Equal(new NodeId("node-b"), resolved.Node);
        Assert.Equal(2, resolved.Generation);
    }

    [Fact]
    public async Task StaleGenerationCannotReplaceCurrentRouteLocation()
    {
        var directory = new InMemoryRouteDirectory();
        var now = DateTimeOffset.UtcNow;
        var route = new RouteKey("room/1");

        await directory.RegisterAsync(
            new RouteLocation(route, "node-a", new NodeEndpoint("in-memory://node-a"), now.AddMinutes(1), generation: 2),
            TestContext.Current.CancellationToken);

        var status = await directory.RegisterAsync(
            new RouteLocation(route, "node-b", new NodeEndpoint("in-memory://node-b"), now.AddMinutes(1), generation: 1),
            TestContext.Current.CancellationToken);
        var resolved = await directory.ResolveAsync(route, now, TestContext.Current.CancellationToken);

        Assert.Equal(RouteRegistrationStatus.StaleLocation, status);
        Assert.NotNull(resolved);
        Assert.Equal(new NodeId("node-a"), resolved.Node);
        Assert.Equal(2, resolved.Generation);
    }

    [Fact]
    public async Task OlderNodeEpochCannotReplaceCurrentRouteLocation()
    {
        var directory = new InMemoryRouteDirectory();
        var now = DateTimeOffset.UtcNow;
        var route = new RouteKey("room/1");

        await directory.RegisterAsync(
            new RouteLocation(route, "node-a", new NodeEndpoint("in-memory://node-a-v2"), now.AddMinutes(1), nodeEpoch: 2, generation: 1),
            TestContext.Current.CancellationToken);

        var status = await directory.RegisterAsync(
            new RouteLocation(route, "node-a", new NodeEndpoint("in-memory://node-a-v1"), now.AddMinutes(1), nodeEpoch: 1, generation: 1),
            TestContext.Current.CancellationToken);
        var resolved = await directory.ResolveAsync(route, now, TestContext.Current.CancellationToken);

        Assert.Equal(RouteRegistrationStatus.StaleLocation, status);
        Assert.NotNull(resolved);
        Assert.Equal(2, resolved.NodeEpoch);
        Assert.Equal("in-memory://node-a-v2", resolved.Endpoint.Address);
    }

    [Fact]
    public async Task NewerNodeEpochCanReplaceSameNodeLocation()
    {
        var directory = new InMemoryRouteDirectory();
        var now = DateTimeOffset.UtcNow;
        var route = new RouteKey("room/1");

        await directory.RegisterAsync(
            new RouteLocation(route, "node-a", new NodeEndpoint("in-memory://node-a-v1"), now.AddMinutes(1), nodeEpoch: 1, generation: 1),
            TestContext.Current.CancellationToken);

        var status = await directory.RegisterAsync(
            new RouteLocation(route, "node-a", new NodeEndpoint("in-memory://node-a-v2"), now.AddMinutes(1), nodeEpoch: 2, generation: 1),
            TestContext.Current.CancellationToken);
        var resolved = await directory.ResolveAsync(route, now, TestContext.Current.CancellationToken);

        Assert.Equal(RouteRegistrationStatus.Registered, status);
        Assert.NotNull(resolved);
        Assert.Equal(2, resolved.NodeEpoch);
        Assert.Equal("in-memory://node-a-v2", resolved.Endpoint.Address);
    }

    [Fact]
    public async Task ExpiredRouteIsUnavailableAndRemoved()
    {
        var directory = new InMemoryRouteDirectory();
        var now = DateTimeOffset.UtcNow;
        var route = new RouteKey("room/1");
        await directory.RegisterAsync(
            new RouteLocation(route, "node-a", new NodeEndpoint("in-memory://node-a"), now.AddSeconds(1)),
            TestContext.Current.CancellationToken);

        var resolved = await directory.ResolveAsync(route, now.AddSeconds(2), TestContext.Current.CancellationToken);
        var expiredAgain = await directory.ExpireAsync(now.AddSeconds(2), TestContext.Current.CancellationToken);

        Assert.Null(resolved);
        Assert.Equal(0, expiredAgain);
    }

    [Fact]
    public async Task RefreshLeaseExtendsOnlyMatchingRouteOwner()
    {
        var directory = new InMemoryRouteDirectory();
        var now = DateTimeOffset.UtcNow;
        var route = new RouteKey("room/1");
        var location = new RouteLocation(
            route,
            "node-a",
            new NodeEndpoint("in-memory://node-a"),
            now.AddSeconds(10),
            nodeEpoch: 1,
            generation: 1);
        await directory.RegisterAsync(location, TestContext.Current.CancellationToken);

        var refreshed = await directory.RefreshLeaseAsync(
            location,
            now.AddMinutes(1),
            now,
            TestContext.Current.CancellationToken);
        var staleRefresh = await directory.RefreshLeaseAsync(
            new RouteLocation(route, "node-a", new NodeEndpoint("in-memory://node-a"), now.AddSeconds(10), nodeEpoch: 0, generation: 1),
            now.AddMinutes(2),
            now,
            TestContext.Current.CancellationToken);
        var resolved = await directory.ResolveAsync(route, now.AddSeconds(30), TestContext.Current.CancellationToken);

        Assert.Equal(RouteLeaseRefreshStatus.Refreshed, refreshed);
        Assert.Equal(RouteLeaseRefreshStatus.StaleLocation, staleRefresh);
        Assert.NotNull(resolved);
        Assert.Equal(now.AddMinutes(1), resolved.ExpiresAt);
    }

    [Fact]
    public async Task ClearByNodeRemovesOnlyMatchingNodeRoutes()
    {
        var directory = new InMemoryRouteDirectory();
        var now = DateTimeOffset.UtcNow;
        await directory.RegisterAsync(
            new RouteLocation("room/1", "node-a", new NodeEndpoint("in-memory://node-a"), now.AddMinutes(1)),
            TestContext.Current.CancellationToken);
        await directory.RegisterAsync(
            new RouteLocation("room/2", "node-b", new NodeEndpoint("in-memory://node-b"), now.AddMinutes(1)),
            TestContext.Current.CancellationToken);

        var removed = await directory.ClearByNodeAsync("node-a", TestContext.Current.CancellationToken);

        Assert.Equal(1, removed);
        Assert.Null(await directory.ResolveAsync("room/1", now, TestContext.Current.CancellationToken));
        Assert.NotNull(await directory.ResolveAsync("room/2", now, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ClearByNodeEpochRemovesOnlyMatchingRestartGeneration()
    {
        var directory = new InMemoryRouteDirectory();
        var now = DateTimeOffset.UtcNow;
        await directory.RegisterAsync(
            new RouteLocation("room/old", "node-a", new NodeEndpoint("in-memory://node-a-v1"), now.AddMinutes(1), nodeEpoch: 1, generation: 1),
            TestContext.Current.CancellationToken);
        await directory.RegisterAsync(
            new RouteLocation("room/new", "node-a", new NodeEndpoint("in-memory://node-a-v2"), now.AddMinutes(1), nodeEpoch: 2, generation: 1),
            TestContext.Current.CancellationToken);

        var removed = await directory.ClearByNodeEpochAsync("node-a", 1, TestContext.Current.CancellationToken);

        Assert.Equal(1, removed);
        Assert.Null(await directory.ResolveAsync("room/old", now, TestContext.Current.CancellationToken));
        Assert.NotNull(await directory.ResolveAsync("room/new", now, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void RouteLocationContainsOnlySerializableLocationState()
    {
        var propertyTypes = typeof(RouteLocation)
            .GetProperties()
            .Select(property => property.PropertyType)
            .ToArray();

        Assert.Contains(typeof(RouteKey), propertyTypes);
        Assert.Contains(typeof(NodeId), propertyTypes);
        Assert.Contains(typeof(NodeEndpoint), propertyTypes);
        Assert.Contains(typeof(DateTimeOffset), propertyTypes);
        Assert.Contains(typeof(long), propertyTypes);
        Assert.DoesNotContain(propertyTypes, type => type.Name.Contains("Actor", StringComparison.Ordinal));
        Assert.DoesNotContain(propertyTypes, type => type.Name.Contains("Thread", StringComparison.Ordinal));
        Assert.DoesNotContain(propertyTypes, type => typeof(Delegate).IsAssignableFrom(type));
    }
}
