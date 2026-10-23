using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Testing.Fixtures.App;
using Lakona.Game.Testing.Fixtures.Hotfix;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Testing.Tests;

public sealed class ActorClusterLifecycleTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Delayed_absent_lookup_starts_again_only_after_previous_activation_fails(bool fail)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var gate = new DelayedLookupGate();
        await using var cluster = new LakonaTestClusterBuilder()
            .AddNode("data-1", "data").AddNode("battle-1", "battle")
            .ConfigureNodes(node =>
            {
                node.UseHotfixAssembly(typeof(CounterBehavior).Assembly);
                node.ConfigureServices((services, _) =>
                {
                    if (node.Roles.Contains("battle")) services.AddSingleton<CounterControl>();
                    else
                    {
                        var registration = services.Last(item => item.ServiceType == typeof(IActorDirectory));
                        services.Remove(registration);
                        services.AddSingleton<IActorDirectory>(provider => new DelayedLookupDirectory(
                            (IActorDirectory)registration.ImplementationFactory!(provider), gate));
                    }
                });
            }).Build();
        await cluster.StartAsync(ct);
        var services = cluster.Node("data-1").Services;
        var actors = services.GetRequiredService<IHotfixRuntimeAccessor>().Current.Services.GetRequiredService<ActorAccess>();
        var id = new CounterId("delayed-ensure");
        var control = cluster.Node("battle-1").Services.GetRequiredService<CounterControl>();
        gate.Enabled = true;
        var first = actors.Place<CounterActor>(id).EnsureAsync(new CounterStartArgs { Value = 42, Wait = true, Fail = fail }, ct).AsTask();
        var second = actors.Place<CounterActor>(id).EnsureAsync(new CounterStartArgs { Value = 77 }, ct).AsTask();
        try
        {
            await gate.BothRead.Task.WaitAsync(ct);
            await control.Entered.WaitAsync(ct);
            Assert.False(second.IsCompleted);
            Assert.Equal(1, control.Starts);
            control.Release();
            if (fail) await Assert.ThrowsAsync<ActorPlacementException>(async () => await first);
            else await first;
            Assert.Equal(1, control.Starts);
            Assert.False(second.IsCompleted);
            gate.ReleaseSecond.TrySetResult();
            await second;
            Assert.Equal(fail ? 2 : 1, control.Starts);
            Assert.Equal(fail ? 77 : 42, (await CounterCalls.AddAsync(actors, id, 0, ct)).Value);
        }
        finally
        {
            control.Release();
            gate.ReleaseSecond.TrySetResult();
        }
    }

    private sealed class DelayedLookupGate
    {
        public bool Enabled;
        public int Reads;
        public TaskCompletionSource BothRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSecond { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class DelayedLookupDirectory(IActorDirectory inner, DelayedLookupGate gate) : IActorDirectory
    {
        public async ValueTask<ActorDirectoryRecord?> ResolveAsync(ActorId id, CancellationToken ct = default)
        {
            var record = await inner.ResolveAsync(id, ct);
            if (!gate.Enabled) return record;
            var read = Interlocked.Increment(ref gate.Reads);
            if (read > 2) return record;
            Assert.Null(record);
            // Both callers observe absence before either can start activation.
            if (read == 2) gate.BothRead.TrySetResult();
            await gate.BothRead.Task.WaitAsync(ct);
            // Hold the captured response until the first activation has completed.
            if (read == 2) await gate.ReleaseSecond.Task.WaitAsync(ct);
            return record;
        }

        public ValueTask<ActorActivationAcquireResult> AcquireAsync(ActorId id, NodeReference owner,
            ActorActivationId activation, CancellationToken ct = default) => inner.AcquireAsync(id, owner, activation, ct);

        public ValueTask<bool> ReleaseAsync(ActorId id, ActorActivationId activation,
            CancellationToken ct = default) => inner.ReleaseAsync(id, activation, ct);
    }

    [Theory]
    [InlineData("data-1", false)]
    [InlineData("battle-1", false)]
    [InlineData("data-1", true)]
    [InlineData("battle-1", true)]
    public async Task Ensure_shares_start_result_and_only_applies_winning_snapshot(string caller, bool fail)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var cluster = CreateCluster(includeSecondBattle: false);
        await cluster.StartAsync(ct);
        var actors = cluster.Node(caller).Services.GetRequiredService<IHotfixRuntimeAccessor>()
            .Current.Services.GetRequiredService<ActorAccess>();
        var id = new CounterId("initial-snapshot");
        var control = cluster.Node("battle-1").Services.GetRequiredService<CounterControl>();
        var args = new CounterStartArgs { Value = 42, Wait = true, Fail = fail,
            Snapshot = new CounterSnapshot { Name = "hero", Items = [new CounterItem { Id = 3, Label = "sword" }], Scores = new() { ["level"] = 9 }, ExternalId = new("external-42") } };
        var first = actors.Place<CounterActor>(id).EnsureAsync(args, ct).AsTask();
        await control.Entered.WaitAsync(ct);
        args.Value = 999;
        args.Snapshot.Items[0].Label = "changed";
        args.Snapshot.Scores["level"] = 99;
        var second = actors.Place<CounterActor>(id).EnsureAsync(new CounterStartArgs { Value = 7 }, ct).AsTask();
        var third = actors.Place<CounterActor>(id).EnsureAsync(ct).AsTask();
        try
        {
            await Task.Delay(100, ct);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.False(third.IsCompleted);
            await Assert.ThrowsAnyAsync<ActorCallException>(async () => await CounterCalls.AddAsync(actors, id, 1, ct));
        }
        finally { control.Release(); }
        if (fail)
        {
            await Assert.ThrowsAnyAsync<Exception>(async () => await first);
            await Assert.ThrowsAnyAsync<Exception>(async () => await second);
            await Assert.ThrowsAnyAsync<Exception>(async () => await third);
            Assert.Equal(1, control.Starts);
            await actors.Place<CounterActor>(id).EnsureAsync(new CounterStartArgs { Value = 11 }, ct);
            Assert.Equal(11, (await CounterCalls.AddAsync(actors, id, 0, ct)).Value);
            Assert.Equal(2, control.Starts);
        }
        else
        {
            await Task.WhenAll(first, second, third);
            await actors.Place<CounterActor>(id).EnsureAsync(new CounterStartArgs { Value = 99 }, ct);
            var reply = await CounterCalls.AddAsync(actors, id, 0, ct);
            Assert.Equal(42, reply.Value);
            Assert.NotNull(reply.Snapshot);
            Assert.Equal("hero", reply.Snapshot.Name);
            Assert.Equal("sword", Assert.Single(reply.Snapshot.Items).Label);
            Assert.Equal(3, reply.Snapshot.Items[0].Id);
            Assert.Equal(9, reply.Snapshot.Scores["level"]);
            Assert.Equal("external-42", reply.Snapshot.ExternalId!.Value);
            Assert.Equal(1, control.Starts);
        }
        await actors.Place<CounterActor>(id).DestroyAsync(ct);
        await actors.Place<CounterActor>(id).EnsureAsync(ct);
        Assert.Equal(0, (await CounterCalls.AddAsync(actors, id, 0, ct)).Value);
    }

    [Fact]
    public async Task GracefulStopDrainsInFlightCallThenRelocatesActor()
    {
        await using var cluster = CreateCluster();
        await cluster.StartAsync(TestContext.Current.CancellationToken);

        var actors = cluster.Node("data-1").Services.GetRequiredService<IHotfixRuntimeAccessor>()
            .Current.Services.GetRequiredService<ActorAccess>();
        var id = new CounterId("graceful");
        var initial = await actors.Place<CounterActor>(id)
            .EnsureAsync(TestContext.Current.CancellationToken);
        var control = cluster.Node("battle-1").Services.GetRequiredService<CounterControl>();
        var call = CounterCalls.WaitAndAddAsync(
            actors,
            id,
            TestContext.Current.CancellationToken).AsTask();
        await control.Entered.WaitAsync(TestContext.Current.CancellationToken);

        var stop = cluster.StopNodeAsync(
            "battle-1",
            TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal("battle-1", initial.Owner.Value);
        Assert.False(stop.IsCompleted);
        await Assert.ThrowsAnyAsync<ActorCallException>(async () =>
            await CounterCalls.AddAsync(
                actors,
                id,
                10,
                TestContext.Current.CancellationToken));

        control.Release();
        Assert.Equal(1, (await call).Value);
        await stop;
        await cluster.WaitForMembershipAsync(TestContext.Current.CancellationToken);

        var relocated = await actors.Place<CounterActor>(id)
            .EnsureAsync(TestContext.Current.CancellationToken);
        Assert.Equal("battle-2", relocated.Owner.Value);
        Assert.Equal(1, (await CounterCalls.AddAsync(
            actors,
            id,
            1,
            TestContext.Current.CancellationToken)).Value);

        await actors.Place<CounterActor>(id)
            .DestroyAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task KillFailsPendingCallAndRestartUsesNewIncarnation()
    {
        await using var cluster = CreateCluster(includeSecondBattle: false);
        await cluster.StartAsync(TestContext.Current.CancellationToken);

        var actors = cluster.Node("data-1").Services.GetRequiredService<IHotfixRuntimeAccessor>()
            .Current.Services.GetRequiredService<ActorAccess>();
        var id = new CounterId("kill-restart");
        var oldNode = cluster.Node("battle-1");
        await actors.Place<CounterActor>(id)
            .EnsureAsync(TestContext.Current.CancellationToken);
        var control = oldNode.Services.GetRequiredService<CounterControl>();
        var call = CounterCalls.WaitAndAddAsync(
            actors,
            id,
            TestContext.Current.CancellationToken).AsTask();
        await control.Entered.WaitAsync(TestContext.Current.CancellationToken);

        var kill = cluster.KillNodeAsync("battle-1", TestContext.Current.CancellationToken);
        try
        {
            await Assert.ThrowsAnyAsync<ActorCallException>(async () =>
                await call.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        }
        finally
        {
            // The harness shares this process: killing the node cannot abort
            // accepted application code. Release it before host disposal drains.
            control.Release();
            await kill.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }

        var restarted = await cluster.StartNodeAsync(
            "battle-1",
            TestContext.Current.CancellationToken);
        await cluster.WaitForMembershipAsync(TestContext.Current.CancellationToken);
        var placement = await actors.Place<CounterActor>(id)
            .EnsureAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(oldNode.Reference, restarted.Reference);
        Assert.Equal("battle-1", placement.Owner.Value);
        Assert.Equal(2, (await CounterCalls.AddAsync(
            actors,
            id,
            2,
            TestContext.Current.CancellationToken)).Value);

        await actors.Place<CounterActor>(id)
            .DestroyAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Cancelling_follower_does_not_cancel_shared_start()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var cluster = CreateCluster(false);
        await cluster.StartAsync(ct);
        var actors = cluster.Node("data-1").Services.GetRequiredService<IHotfixRuntimeAccessor>()
            .Current.Services.GetRequiredService<ActorAccess>();
        var id = new CounterId("cancel-follower");
        var control = cluster.Node("battle-1").Services.GetRequiredService<CounterControl>();
        var first = actors.Place<CounterActor>(id).EnsureAsync(new CounterStartArgs { Wait = true, Value = 8 }, ct).AsTask();
        await control.Entered.WaitAsync(ct);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var follower = actors.Place<CounterActor>(id).EnsureAsync(cancelled.Token).AsTask();
        try
        {
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<Exception>(async () => await follower);
            Assert.False(first.IsCompleted);
        }
        finally { control.Release(); }
        await first;
        Assert.Equal(8, (await CounterCalls.AddAsync(actors, id, 0, ct)).Value);
        Assert.Equal(1, control.Starts);
    }

    [Fact]
    public async Task Wrong_start_contract_fails_without_leaving_an_activation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var cluster = CreateCluster(false);
        await cluster.StartAsync(ct);
        var actors = cluster.Node("data-1").Services.GetRequiredService<IHotfixRuntimeAccessor>()
            .Current.Services.GetRequiredService<ActorAccess>();
        var id = new CounterId("wrong-contract");
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await actors.Place<CounterActor>(id).EnsureAsync(new AddCounterRequest { Delta = 99 }, ct));
        await actors.Place<CounterActor>(id).EnsureAsync(ct);
        Assert.Equal(0, (await CounterCalls.AddAsync(actors, id, 0, ct)).Value);
    }

    private static LakonaTestCluster CreateCluster(bool includeSecondBattle = true)
    {
        var builder = new LakonaTestClusterBuilder()
            .AddNode("data-1", "data")
            .AddNode("battle-1", "battle");
        if (includeSecondBattle)
        {
            builder.AddNode("battle-2", "battle");
        }

        return builder
            .ConfigureNodes(node =>
            {
                node.UseHotfixAssembly(typeof(CounterBehavior).Assembly);
                if (node.Roles.Contains("battle", StringComparer.Ordinal))
                {
                    node.ConfigureServices(static (services, _) =>
                        services.AddSingleton<CounterControl>());
                }
            })
            .Build();
    }
}
