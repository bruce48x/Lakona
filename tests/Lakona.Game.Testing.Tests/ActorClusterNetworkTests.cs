using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Testing.Fixtures.App;
using Lakona.Game.Testing.Fixtures.Hotfix;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Lakona.Game.Testing.Tests;

public sealed class ActorClusterNetworkTests
{
    [Fact]
    public async Task PartitionFailsAnActorCallAndHealRestoresTheSameRoute()
    {
        await using var cluster = CreateCluster();
        await cluster.StartAsync(TestContext.Current.CancellationToken);
        var actors = cluster.Node("data-1").Services.GetRequiredService<ActorAccess>();
        var id = new CounterId("partition");
        await actors.Place<CounterActor>(id)
            .EnsureAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, (await CounterCalls.AddAsync(
            actors,
            id,
            1,
            TestContext.Current.CancellationToken)).Value);

        cluster.Network.Partition("data-1", "battle-1");
        await Assert.ThrowsAsync<NodeUnavailableException>(async () =>
            await CounterCalls.AddAsync(
                actors,
                id,
                10,
                TestContext.Current.CancellationToken));

        cluster.Network.Heal("data-1", "battle-1");
        Assert.Equal(3, (await CounterCalls.AddAsync(
            actors,
            id,
            2,
            TestContext.Current.CancellationToken)).Value);

        await actors.Place<CounterActor>(id)
            .DestroyAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CallerCancellationDoesNotLetALateReplyCompleteAnotherCall()
    {
        await using var cluster = CreateCluster();
        await cluster.StartAsync(TestContext.Current.CancellationToken);
        var actors = cluster.Node("data-1").Services.GetRequiredService<ActorAccess>();
        var id = new CounterId("late-cancel");
        await actors.Place<CounterActor>(id)
            .EnsureAsync(TestContext.Current.CancellationToken);
        var control = cluster.Node("battle-1").Services.GetRequiredService<CounterControl>();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var cancelledCall = CounterCalls.WaitIgnoringCancellationAndAddAsync(
            actors,
            id,
            cancellation.Token).AsTask();
        await control.Entered.WaitAsync(TestContext.Current.CancellationToken);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelledCall);
        control.Release();
        await control.Completed.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, (await CounterCalls.AddAsync(
            actors,
            id,
            1,
            TestContext.Current.CancellationToken)).Value);

        await actors.Place<CounterActor>(id)
            .DestroyAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ActorDeadlineReturnsTimeoutAndIgnoresLateReply()
    {
        await using var cluster = CreateCluster(callTimeout: TimeSpan.FromMilliseconds(200));
        await cluster.StartAsync(TestContext.Current.CancellationToken);
        var actors = cluster.Node("data-1").Services.GetRequiredService<ActorAccess>();
        var id = new CounterId("late-timeout");
        await actors.Place<CounterActor>(id)
            .EnsureAsync(TestContext.Current.CancellationToken);
        var control = cluster.Node("battle-1").Services.GetRequiredService<CounterControl>();
        var timedOutCall = CounterCalls.WaitIgnoringCancellationAndAddAsync(
            actors,
            id,
            TestContext.Current.CancellationToken).AsTask();
        await control.Entered.WaitAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ActorCallTimeoutException>(async () => await timedOutCall);
        control.Release();
        await control.Completed.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, (await CounterCalls.AddAsync(
            actors,
            id,
            1,
            TestContext.Current.CancellationToken)).Value);

        await actors.Place<CounterActor>(id)
            .DestroyAsync(TestContext.Current.CancellationToken);
    }

    private static LakonaTestCluster CreateCluster(TimeSpan? callTimeout = null) =>
        new LakonaTestClusterBuilder()
            .AddNode("data-1", "data")
            .AddNode("battle-1", "battle")
            .ConfigureNodes(node =>
            {
                node.UseHotfixAssembly(typeof(CounterBehavior).Assembly);
                node.ConfigureServices((services, _) =>
                {
                    if (node.Roles.Contains("battle", StringComparer.Ordinal))
                    {
                        services.AddSingleton<CounterControl>();
                    }

                    if (callTimeout is { } timeout)
                    {
                        services.Replace(ServiceDescriptor.Singleton(
                            new RemoteActorOptions { DefaultTimeout = timeout }));
                    }
                });
            })
            .Build();
}
