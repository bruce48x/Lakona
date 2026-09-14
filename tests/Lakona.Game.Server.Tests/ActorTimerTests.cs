using System.Collections.Concurrent;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Timers;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Scanning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed partial class ActorTimerTests
{
    [Fact]
    public async Task Disposing_unstarted_scheduler_detaches_activation_cancellation()
    {
        await using var fixture = new Fixture();
        var actor = await fixture.CreateActorAsync("unstarted");
        var timer = await fixture.CreateTimerAsync(actor, TimeSpan.FromDays(1));
        await fixture.Scheduler.DisposeAsync();
        Assert.False(fixture.Scheduler.Contains(timer));
        await fixture.Catalog.DestroyAsync<TimerActor>(actor.Context.Id, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Timer_can_cancel_itself_without_waiting_for_its_own_turn()
    {
        await using var fixture = new Fixture();
        await fixture.StartAsync();
        fixture.Probe.CancelSelf = true;
        var actor = await fixture.CreateActorAsync("self-cancel");
        var timer = await fixture.CreateTimerAsync(actor, TimeSpan.Zero, TimeSpan.FromMilliseconds(10));
        await fixture.Probe.NextAsync();
        await fixture.Scheduler.StopAsync(TestContext.Current.CancellationToken);
        Assert.Single(fixture.Probe.Calls);
        Assert.False(fixture.Scheduler.Contains(timer));
    }

    [Fact]
    public async Task Full_mailbox_retains_tick_and_does_not_block_other_actor_timers()
    {
        await using var fixture = new Fixture();
        var first = await fixture.CreateActorAsync("first");
        var second = await fixture.CreateActorAsync("second");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.CreateTimerAsync(first, TimeSpan.Zero);
        await fixture.CreateTimerAsync(second, TimeSpan.Zero);
        var busy = fixture.Catalog.AskAsync<TimerActor, int>(first.Context.Id, async (_, _) =>
        {
            entered.SetResult();
            await release.Task;
            return 0;
        }, TestContext.Current.CancellationToken).AsTask();
        await entered.Task;
        try
        {
            Assert.Equal(ActorTellResult.MailboxFull, fixture.Catalog.TryTell<TimerActor>(
                first.Context.Id, static (_, _) => default, TestContext.Current.CancellationToken));
            await fixture.StartAsync();
            await fixture.Probe.NextAsync();
            Assert.Equal(["second"], fixture.Probe.Calls.Select(call => call.Actor.Context.Id.Value));
            Assert.Single(fixture.Probe.Calls);
        }
        finally { release.TrySetResult(); }
        await busy;
        await fixture.Probe.NextAsync();
        Assert.Same(first, fixture.Probe.Calls.Last().Actor);
    }

    [Fact]
    public async Task Destroying_activation_cancels_its_timers_and_does_not_target_replacement()
    {
        await using var fixture = new Fixture();
        await fixture.StartAsync();
        var actor = await fixture.CreateActorAsync("same-id");
        var timer = await fixture.CreateTimerAsync(actor, TimeSpan.FromMilliseconds(100));
        await fixture.Catalog.DestroyAsync<TimerActor>(actor.Context.Id, TestContext.Current.CancellationToken);
        Assert.False(fixture.Scheduler.Contains(timer));
        var replacement = await fixture.CreateActorAsync("same-id");
        Assert.NotSame(actor, replacement);
        await Task.Delay(160, TestContext.Current.CancellationToken);
        Assert.Empty(fixture.Probe.Calls);
        await fixture.CreateTimerAsync(replacement, TimeSpan.Zero);
        await fixture.Probe.NextAsync();
        Assert.Same(replacement, fixture.Probe.Calls.Single().Actor);
    }

    [Fact]
    public async Task Running_callback_holds_capacity_until_actual_completion_after_destroy()
    {
        await using var fixture = new Fixture(maxTimers: 1);
        await fixture.StartAsync();
        var actor = await fixture.CreateActorAsync("slow");
        fixture.Probe.Block = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = await fixture.CreateTimerAsync(actor, TimeSpan.Zero, TimeSpan.FromMilliseconds(10));
        await fixture.Probe.NextAsync();
        fixture.Scheduler.Destroy(timer);
        Assert.True(fixture.Probe.Calls.Single().CancellationToken.IsCancellationRequested);
        try
        {
            var other = await fixture.CreateActorAsync("other");
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.CreateTimerAsync(other, TimeSpan.Zero));
            await Task.Delay(60, TestContext.Current.CancellationToken);
            Assert.Single(fixture.Probe.Calls);
        }
        finally { fixture.Probe.Block.TrySetResult(); }
    }

    [Fact]
    public async Task Queued_tick_acquires_current_behavior_only_when_actor_executes()
    {
        var observer = new ContractObserver();
        await using var fixture = new Fixture(observer: observer);
        var actor = await fixture.CreateActorAsync("reload");
        await fixture.CreateTimerAsync(actor, TimeSpan.Zero);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = fixture.Catalog.AskAsync<TimerActor, int>(actor.Context.Id, async (_, _) =>
        {
            entered.SetResult();
            await release.Task;
            return 0;
        }, TestContext.Current.CancellationToken).AsTask();
        await Wait(entered.Task);
        try
        {
            await fixture.StartAsync();
            await observer.NextStartAsync();
            fixture.Reload(2);
        }
        finally { release.TrySetResult(); }
        await Wait(busy);
        await fixture.Probe.NextAsync();
        Assert.Equal(2, fixture.Probe.Calls.Single().Generation);
    }

    [Fact]
    public async Task Creation_requires_own_active_turn_and_callback_is_not_an_actor_rpc()
    {
        Assert.Throws<InvalidOperationException>(() => new TimerActor().CreateOnceTimer(
            static (TimerBehavior behavior) => behavior.TickAsync, TimeSpan.Zero, 1, TestContext.Current.CancellationToken));
        await using var fixture = new Fixture();
        var actor = await fixture.CreateActorAsync("owner");
        using var lease = fixture.AcquireCurrent();
        using var scope = LakonaTimerRuntime.Enter(fixture.Backend, lease);
        Assert.Throws<InvalidOperationException>(() => actor.CreateOnceTimer(
            static (TimerBehavior behavior) => behavior.TickAsync, TimeSpan.Zero, 1, TestContext.Current.CancellationToken));
        var scan = HotfixBehaviorScanner.Scan(typeof(TimerBehavior).Assembly, [typeof(TimerBehavior)]);
        Assert.Empty(scan.Diagnostics);
        Assert.Empty(scan.ActorMethods);
        Assert.Single(scan.TimerMethods);
    }

    public sealed class TimerActor : Actor<string>;

    [HotfixBehaviorOf(typeof(TimerActor))]
    public sealed partial class TimerBehavior(Probe probe, Generation generation)
    {
        [ActorTimer]
        public async ValueTask TickAsync(TimerActor actor, TimerTick<int> tick)
        {
            if (probe.CancelSelf) actor.DestroyTimer(tick.TimerId);
            probe.Calls.Enqueue(new Call(actor, generation.Value, tick.CancellationToken));
            probe.Available.Release();
            if (probe.OnTick is { } onTick) await onTick(actor, tick);
            // Deliberately ignores cancellation to verify actual-completion tracking.
            if (probe.Block is { } block) await block.Task;
        }
    }

    public sealed record Generation(int Value);
    public sealed record Call(TimerActor Actor, int Generation, CancellationToken CancellationToken);
    public sealed class Probe
    {
        public readonly ConcurrentQueue<Call> Calls = new();
        public readonly SemaphoreSlim Available = new(0);
        public TaskCompletionSource? Block;
        public bool CancelSelf;
        public Func<TimerActor, TimerTick<int>, ValueTask>? OnTick;
        public async Task NextAsync() => Assert.True(await Available.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    private sealed class Fixture : IAsyncDisposable, IHotfixRuntimeAccessor
    {
        private readonly ServiceProvider services;
        private readonly List<ServiceProvider> generationServices = [];
        private readonly List<HotfixDispatchTable> tables = [];
        public Probe Probe { get; } = new();
        public ActorActivationCatalog Catalog { get; }
        public LakonaTimerScheduler Scheduler { get; }
        public LakonaTimerBackend Backend { get; }
        public HotfixRuntimeSnapshot Current { get; private set; } = null!;

        public Fixture(int maxTimers = 100, TimeProvider? time = null, int mailboxCapacity = 1,
            ILakonaTimerSchedulerObserver? observer = null)
        {
            services = new ServiceCollection().AddLakonaGameServerActors(options =>
            {
                options.MailboxCapacity = mailboxCapacity;
            }).BuildServiceProvider();
            Catalog = services.GetRequiredService<ActorActivationCatalog>();
            Reload(1);
            Scheduler = new LakonaTimerScheduler(this, time ?? TimeProvider.System,
                new LakonaTimerOptions { DispatchQueueCapacity = 1, MaxActiveTimers = maxTimers },
                observer, NullLogger<LakonaTimerScheduler>.Instance);
            Backend = new LakonaTimerBackend(Scheduler);
        }

        public void Reload(int version)
        {
            var provider = new ServiceCollection().AddSingleton(Probe).AddSingleton(new Generation(version)).BuildServiceProvider();
            generationServices.Add(provider);
            var scan = HotfixBehaviorScanner.Scan(typeof(TimerBehavior).Assembly, [typeof(TimerBehavior)]);
            Assert.Empty(scan.Diagnostics);
            var table = new HotfixDispatchTable(version, scan.Methods, scan.Services, scan.ActorMethods,
                scan.ActorLifecycles, scan.TimerMethods);
            table.ValidateModuleActivation(provider);
            tables.Add(table);
            Current = new HotfixRuntimeSnapshot(new HotfixServiceInvoker(table), provider, table, provider,
                typeof(TimerBehavior).Assembly, null, null, null, false, null);
        }

        public HotfixRuntimeSnapshotLease AcquireCurrent() => Current.AcquireLease();
        public Task StartAsync() => Scheduler.StartAsync(TestContext.Current.CancellationToken);
        public async Task<TimerActor> CreateActorAsync(string key)
        {
            var id = ActorId.From(key);
            await Catalog.CreateAsync<TimerActor>(id);
            return await Catalog.AskAsync<TimerActor, TimerActor>(id, static (actor, _) => new ValueTask<TimerActor>(actor));
        }

        public ValueTask<TimerId> CreateTimerAsync(TimerActor actor, TimeSpan due, TimeSpan? period = null) =>
            Catalog.AskAsync<TimerActor, TimerId>(actor.Context.Id, (self, _) =>
            {
                using var lease = AcquireCurrent();
                using var scope = LakonaTimerRuntime.Enter(Backend, lease);
                return new ValueTask<TimerId>(period is { } interval
                    ? self.CreatePeriodicTimer(static (TimerBehavior behavior) => behavior.TickAsync, due, interval, 1)
                    : self.CreateOnceTimer(static (TimerBehavior behavior) => behavior.TickAsync, due, 1));
            });

        public async ValueTask DisposeAsync()
        {
            Probe.Block?.TrySetResult();
            await Scheduler.DisposeAsync();
            await services.DisposeAsync();
            foreach (var table in tables) await table.DisposeAsync();
            foreach (var provider in generationServices) await provider.DisposeAsync();
            Probe.Available.Dispose();
        }
    }
}
