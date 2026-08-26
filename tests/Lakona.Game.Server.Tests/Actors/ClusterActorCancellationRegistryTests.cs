using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class ClusterActorCancellationRegistryTests
{
    [Fact]
    public void Cancel_before_register_is_observed_by_late_request()
    {
        using var registry = new ClusterActorCancellationRegistry(TimeProvider.System);
        var invocationId = Guid.NewGuid();

        registry.Cancel(invocationId);
        using var registration = registry.Register(
            invocationId,
            TestContext.Current.CancellationToken);

        Assert.True(registration.Token.IsCancellationRequested);
    }

    [Fact]
    public void Cancel_after_register_cancels_active_request()
    {
        using var registry = new ClusterActorCancellationRegistry(TimeProvider.System);
        var invocationId = Guid.NewGuid();
        using var registration = registry.Register(
            invocationId,
            TestContext.Current.CancellationToken);

        registry.Cancel(invocationId);

        Assert.True(registration.Token.IsCancellationRequested);
    }

    [Fact]
    public void Parent_cancellation_is_linked_without_marking_another_request()
    {
        using var registry = new ClusterActorCancellationRegistry(TimeProvider.System);
        using var parent = new CancellationTokenSource();
        using var first = registry.Register(Guid.NewGuid(), parent.Token);
        using var second = registry.Register(
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        parent.Cancel();

        Assert.True(first.Token.IsCancellationRequested);
        Assert.False(second.Token.IsCancellationRequested);
    }
}
