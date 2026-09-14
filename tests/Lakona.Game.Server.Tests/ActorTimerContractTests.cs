using System.Collections.Concurrent;
using System.Threading.Channels;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Timers;
using Xunit;

namespace Lakona.Game.Server.Tests;

// Inspired by Orleans v10.2.2 TimerOrleansTest: real activation/mailbox execution,
// callback gates, explicit lifecycle signals, and virtual time rather than sleeps.
// Lakona deliberately uses non-reentrant callbacks and start-based periodic cadence.
public sealed partial class ActorTimerTests
{
    private static CancellationToken TestCancellation => TestContext.Current.CancellationToken;
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task Wait(Task task) => task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellation);

    [Fact]
    public async Task Awaited_cross_actor_call_preserves_owner_and_hotfix_scope_for_child_timer()
    {
        await using var fixture = new Fixture();
        var owner = await fixture.CreateActorAsync("parent");
        var other = await fixture.CreateActorAsync("callee");
        var finished = Signal();
        TimerId parent = default;
        TimerId child = default;
        fixture.Probe.OnTick = async (self, tick) =>
        {
            Assert.Same(owner, self);
            if (tick.TimerId == parent)
            {
                var context = LakonaTimerExecutionScope.GetActiveContext();
                Assert.Equal(other.Context.Id, await fixture.Catalog.AskAsync<TimerActor, ActorId>(
                    other.Context.Id, static (actor, _) => new ValueTask<ActorId>(actor.Context.Id), tick.CancellationToken));
                await Task.Yield();
                Assert.Same(context.RuntimeContext, LakonaTimerExecutionScope.GetActiveContext().RuntimeContext);
                child = self.CreateOnceTimer(static (TimerBehavior behavior) => behavior.TickAsync,
                    TimeSpan.Zero, 2, tick.CancellationToken);
                Assert.Throws<InvalidOperationException>(() => other.CreateOnceTimer(
                    static (TimerBehavior behavior) => behavior.TickAsync, TimeSpan.Zero, 3, tick.CancellationToken));
            }
            else
            {
                Assert.Equal(child, tick.TimerId);
                Assert.Equal(2, tick.Args);
                finished.TrySetResult();
            }
        };
        parent = await fixture.CreateTimerAsync(owner, TimeSpan.Zero);
        await fixture.StartAsync();
        await Wait(finished.Task);
        await fixture.Scheduler.StopAsync(TestCancellation);
        Assert.Equal(2, fixture.Probe.Calls.Count);
    }

    [Fact]
    public async Task Multiple_timers_and_ordinary_messages_never_overlap_across_awaits()
    {
        const int count = 16;
        var observer = new ContractObserver();
        await using var fixture = new Fixture(mailboxCapacity: 32, observer: observer);
        var owner = await fixture.CreateActorAsync("serialized");
        var entered = Signal();
        var release = Signal();
        var completed = Signal();
        var active = 0;
        var calls = 0;
        fixture.Probe.OnTick = async (_, _) =>
        {
            Assert.Equal(1, Interlocked.Increment(ref active));
            try
            {
                entered.TrySetResult();
                await release.Task;
                await Task.Yield();
                if (Interlocked.Increment(ref calls) == count) completed.TrySetResult();
            }
            finally { Interlocked.Decrement(ref active); }
        };
        for (var i = 0; i < count; i++) await fixture.CreateTimerAsync(owner, TimeSpan.Zero);
        await fixture.StartAsync();
        await Wait(entered.Task);
        Task<int>? ordinary = null;
        try
        {
            for (var i = 0; i < count; i++) await observer.NextStartAsync();
            ordinary = fixture.Catalog.AskAsync<TimerActor, int>(owner.Context.Id, async (_, _) =>
            {
                Assert.Equal(1, Interlocked.Increment(ref active));
                try { await Task.Yield(); return calls; }
                finally { Interlocked.Decrement(ref active); }
            }, TestCancellation).AsTask();
            Assert.False(ordinary.IsCompleted);
            Assert.Equal(1, active);
        }
        finally { release.TrySetResult(); }
        await Wait(completed.Task);
        await Wait(ordinary!);
        await fixture.Scheduler.StopAsync(TestCancellation);
        Assert.Equal(count, calls);
        Assert.Empty(observer.Failures);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_while_waiting_for_mailbox_skips_only_the_selected_timer(bool stopActivation)
    {
        var observer = new ContractObserver();
        await using var fixture = new Fixture(observer: observer);
        var owner = await fixture.CreateActorAsync("waiting");
        var first = await fixture.CreateTimerAsync(owner, TimeSpan.Zero);
        var second = await fixture.CreateTimerAsync(owner, TimeSpan.Zero);
        var entered = Signal();
        var release = Signal();
        var received = new ConcurrentQueue<TimerId>();
        fixture.Probe.OnTick = (_, tick) => { received.Enqueue(tick.TimerId); return default; };
        var busy = fixture.Catalog.AskAsync<TimerActor, int>(owner.Context.Id, async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            return 0;
        }, TestCancellation).AsTask();
        await Wait(entered.Task);
        Task? destroy = null;
        try
        {
            await fixture.StartAsync();
            await observer.NextStartAsync();
            await observer.NextStartAsync();
            if (stopActivation)
            {
                var stopped = Signal();
                using var registration = owner.Context.TimerOwner!.Stopping.Register(() => stopped.TrySetResult());
                destroy = fixture.Catalog.DestroyAsync<TimerActor>(owner.Context.Id, TestCancellation).AsTask();
                await Wait(stopped.Task);
            }
            else fixture.Scheduler.Destroy(first);
        }
        finally { release.TrySetResult(); }
        await Wait(busy);
        if (destroy is not null) await Wait(destroy);
        else
        {
            await fixture.Probe.NextAsync();
            await fixture.Catalog.AskAsync<TimerActor, int>(owner.Context.Id,
                static (_, _) => new ValueTask<int>(0), TestCancellation);
        }
        await fixture.Scheduler.StopAsync(TestCancellation);
        Assert.Equal(stopActivation ? Array.Empty<TimerId>() : [second], received.ToArray());
        Assert.Empty(observer.Failures);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shutdown_tracks_actual_completion_even_if_the_stop_caller_cancels(bool cancelWait)
    {
        await using var fixture = new Fixture();
        var owner = await fixture.CreateActorAsync("shutdown");
        var entered = Signal();
        var canceled = Signal();
        var release = Signal();
        fixture.Probe.OnTick = async (_, tick) =>
        {
            using var registration = tick.CancellationToken.Register(() => canceled.TrySetResult());
            entered.TrySetResult();
            await release.Task; // Deliberately does not observe cancellation.
        };
        await fixture.CreateTimerAsync(owner, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        await fixture.StartAsync();
        await Wait(entered.Task);
        using var stopCancellation = new CancellationTokenSource();
        var stop = fixture.Scheduler.StopAsync(stopCancellation.Token);
        try
        {
            await Wait(canceled.Task);
            Assert.False(stop.IsCompleted);
            if (cancelWait)
            {
                stopCancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stop);
                stop = fixture.Scheduler.StopAsync(TestCancellation);
                Assert.False(stop.IsCompleted);
            }
        }
        finally { release.TrySetResult(); }
        await Wait(stop);
        Assert.Empty(fixture.Scheduler.Descriptors);
        Assert.Single(fixture.Probe.Calls);
    }

    [Fact]
    public async Task Callback_self_deactivation_cancels_all_sibling_timers()
    {
        await using var fixture = new Fixture();
        var owner = await fixture.CreateActorAsync("self-stop");
        var stopped = Signal();
        using var registration = owner.Context.TimerOwner!.Stopping.Register(() => stopped.TrySetResult());
        fixture.Probe.OnTick = async (self, _) =>
        {
            await Task.Yield();
            self.Context.RequestDeactivation();
        };
        for (var i = 0; i < 8; i++)
            await fixture.CreateTimerAsync(owner, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        await fixture.StartAsync();
        await Wait(stopped.Task);
        await fixture.Scheduler.StopAsync(TestCancellation);
        Assert.Single(fixture.Probe.Calls);
        Assert.Empty(fixture.Scheduler.Descriptors);
    }

    [Fact]
    public async Task Canceling_creation_token_after_success_does_not_cancel_timer_lifetime()
    {
        await using var fixture = new Fixture();
        var owner = await fixture.CreateActorAsync("creation-token");
        using var creation = new CancellationTokenSource();
        await fixture.Catalog.AskAsync<TimerActor, TimerId>(owner.Context.Id, (self, _) =>
        {
            using var lease = fixture.AcquireCurrent();
            using var scope = LakonaTimerRuntime.Enter(fixture.Backend, lease);
            return new ValueTask<TimerId>(self.CreateOnceTimer(static (TimerBehavior behavior) => behavior.TickAsync,
                TimeSpan.Zero, 1, creation.Token));
        }, TestCancellation);
        creation.Cancel();
        await fixture.StartAsync();
        await fixture.Probe.NextAsync();
        Assert.False(fixture.Probe.Calls.Single().CancellationToken.IsCancellationRequested);
    }

    [Theory]
    [InlineData(17)]
    [InlineData(83)]
    [InlineData(211)]
    public async Task Many_activations_deliver_each_uncanceled_timer_exactly_once(int seed)
    {
        const int actors = 12;
        const int perActor = 4;
        var observer = new ContractObserver();
        await using var fixture = new Fixture(observer: observer);
        var random = new Random(seed);
        var expected = new Dictionary<TimerId, TimerActor>();
        var received = new ConcurrentDictionary<TimerId, TimerActor>();
        fixture.Probe.OnTick = async (self, tick) =>
        {
            await Task.Yield();
            Assert.True(received.TryAdd(tick.TimerId, self));
            Assert.Same(expected[tick.TimerId], self);
        };
        for (var actorIndex = 0; actorIndex < actors; actorIndex++)
        {
            var owner = await fixture.CreateActorAsync($"{seed}-{actorIndex}");
            for (var timerIndex = 0; timerIndex < perActor; timerIndex++)
            {
                var timer = await fixture.CreateTimerAsync(owner, TimeSpan.Zero);
                if (random.Next(3) == 0) fixture.Scheduler.Destroy(timer);
                else expected.Add(timer, owner);
            }
        }
        await fixture.StartAsync();
        for (var i = 0; i < expected.Count; i++) await observer.NextCompletionAsync();
        await fixture.Scheduler.StopAsync(TestCancellation);
        Assert.Equal(expected.Keys.OrderBy(id => id.ToString()), received.Keys.OrderBy(id => id.ToString()));
        Assert.Empty(observer.Failures);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10000)]
    public async Task Periodic_timer_preserves_identity_payload_and_exact_virtual_deadlines(int periodMilliseconds)
    {
        var time = new LakonaTimerSchedulerTests.ManualTimeProvider(DateTimeOffset.Parse("2026-09-14T00:00:00Z"));
        var observer = new ContractObserver();
        await using var fixture = new Fixture(time: time, observer: observer);
        var owner = await fixture.CreateActorAsync("virtual-periods");
        var period = TimeSpan.FromMilliseconds(periodMilliseconds);
        var deadline = time.GetUtcNow().AddSeconds(5);
        TimerId timer = default;
        fixture.Probe.OnTick = async (self, tick) =>
        {
            await Task.Yield();
            Assert.Same(owner, self);
            Assert.Equal(timer, tick.TimerId);
            Assert.Equal(1, tick.Args);
            Assert.Equal(deadline, tick.DueAtUtc);
            Assert.Equal(deadline, tick.ObservedAtUtc);
        };
        timer = await fixture.CreateTimerAsync(owner, TimeSpan.FromSeconds(5), period);
        await fixture.StartAsync();
        for (var round = 1; round <= 10; round++)
        {
            await time.WaitForDeadlineAsync(deadline, TestCancellation);
            time.Advance(deadline - time.GetUtcNow());
            await observer.NextCompletionAsync();
            Assert.Equal(round, fixture.Probe.Calls.Count);
            Assert.Empty(observer.Failures);
            deadline += period;
        }
        fixture.Scheduler.Destroy(timer);
        time.Advance(TimeSpan.FromDays(10));
        await fixture.Scheduler.StopAsync(TestCancellation);
        Assert.Equal(10, fixture.Probe.Calls.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_callback_does_not_deactivate_owner_or_retry_business_work(bool afterAwait)
    {
        var time = new LakonaTimerSchedulerTests.ManualTimeProvider(DateTimeOffset.Parse("2026-09-14T00:00:00Z"));
        var observer = new ContractObserver();
        await using var fixture = new Fixture(time: time, observer: observer);
        var owner = await fixture.CreateActorAsync("throwing");
        var origin = time.GetUtcNow();
        fixture.Probe.OnTick = async (self, _) =>
        {
            self.Context.RequestDeactivation();
            if (afterAwait) await Task.Yield();
            throw new InvalidOperationException("business failure");
        };
        await fixture.CreateTimerAsync(owner, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        await fixture.StartAsync();
        for (var round = 1; round <= 2; round++)
        {
            await observer.NextCompletionAsync();
            await time.WaitForDeadlineAsync(origin.AddSeconds(round), TestCancellation);
            Assert.Equal(round, fixture.Probe.Calls.Count);
            Assert.Equal(round, observer.Failures.Count);
            Assert.All(observer.Failures, exception => Assert.Equal("business failure", exception.Message));
            Assert.Equal(owner.Context.Id, await fixture.Catalog.AskAsync<TimerActor, ActorId>(owner.Context.Id,
                static (actor, _) => new ValueTask<ActorId>(actor.Context.Id), TestCancellation));
            if (round == 1) time.Advance(TimeSpan.FromSeconds(1));
        }
        await fixture.Scheduler.StopAsync(TestCancellation);
    }

    [Fact]
    public async Task Completed_callback_cannot_use_escaped_owner_or_hotfix_scope()
    {
        var observer = new ContractObserver();
        await using var fixture = new Fixture(observer: observer);
        var owner = await fixture.CreateActorAsync("escaped-context");
        var release = Signal();
        Task? escaped = null;
        fixture.Probe.OnTick = (self, _) =>
        {
            escaped = Task.Run(async () =>
            {
                await release.Task;
                Assert.Throws<InvalidOperationException>(() => self.CreateOnceTimer(
                    static (TimerBehavior behavior) => behavior.TickAsync, TimeSpan.Zero, 1, TestCancellation));
                Assert.Throws<InvalidOperationException>(() => LakonaTimerExecutionScope.GetActiveContext());
            }, TestCancellation);
            return default;
        };
        await fixture.CreateTimerAsync(owner, TimeSpan.Zero);
        await fixture.StartAsync();
        await observer.NextCompletionAsync();
        release.TrySetResult();
        await Wait(escaped!);
        Assert.Single(fixture.Probe.Calls);
    }

    [Theory]
    [InlineData("negative-due")]
    [InlineData("zero-period")]
    [InlineData("negative-period")]
    [InlineData("overflowing-due")]
    [InlineData("null-selector")]
    [InlineData("canceled")]
    public async Task Rejected_creation_does_not_consume_capacity(string invalid)
    {
        await using var fixture = new Fixture(maxTimers: 1);
        var owner = await fixture.CreateActorAsync("invalid");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        async Task CreateInvalidAsync() => await fixture.Catalog.AskAsync<TimerActor, TimerId>(owner.Context.Id, (self, _) =>
        {
            using var lease = fixture.AcquireCurrent();
            using var scope = LakonaTimerRuntime.Enter(fixture.Backend, lease);
            return new ValueTask<TimerId>(invalid switch
            {
                "negative-due" => self.CreateOnceTimer(static (TimerBehavior b) => b.TickAsync, TimeSpan.FromTicks(-1), 1, TestCancellation),
                "overflowing-due" => self.CreateOnceTimer(static (TimerBehavior b) => b.TickAsync, TimeSpan.MaxValue, 1, TestCancellation),
                "zero-period" => self.CreatePeriodicTimer(static (TimerBehavior b) => b.TickAsync, TimeSpan.Zero, TimeSpan.Zero, 1, TestCancellation),
                "negative-period" => self.CreatePeriodicTimer(static (TimerBehavior b) => b.TickAsync, TimeSpan.Zero, TimeSpan.FromTicks(-1), 1, TestCancellation),
                "null-selector" => self.CreateOnceTimer<TimerActor, TimerBehavior, int>(null!, TimeSpan.Zero, 1, TestCancellation),
                _ => self.CreateOnceTimer(static (TimerBehavior b) => b.TickAsync, TimeSpan.Zero, 1, canceled.Token)
            });
        }, TestCancellation);
        if (invalid == "canceled") await Assert.ThrowsAnyAsync<OperationCanceledException>(CreateInvalidAsync);
        else await Assert.ThrowsAnyAsync<ArgumentException>(CreateInvalidAsync);
        Assert.Empty(fixture.Scheduler.Descriptors);
        await fixture.CreateTimerAsync(owner, TimeSpan.Zero);
        await fixture.StartAsync();
        await fixture.Probe.NextAsync();
        Assert.Single(fixture.Probe.Calls);
    }

    private sealed class ContractObserver : ILakonaTimerSchedulerObserver
    {
        private readonly Channel<LakonaTimerDispatchObservation> starts = Channel.CreateUnbounded<LakonaTimerDispatchObservation>();
        private readonly Channel<LakonaTimerDispatchObservation> completions = Channel.CreateUnbounded<LakonaTimerDispatchObservation>();
        public ConcurrentQueue<Exception> Failures { get; } = new();
        public Task<LakonaTimerDispatchObservation> NextStartAsync() => starts.Reader.ReadAsync(TestCancellation).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestCancellation);
        public Task<LakonaTimerDispatchObservation> NextCompletionAsync() => completions.Reader.ReadAsync(TestCancellation).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestCancellation);
        public void OnDispatchStarted(LakonaTimerDispatchObservation value) => starts.Writer.TryWrite(value);
        public void OnDispatchCompleted(LakonaTimerDispatchObservation value) => completions.Writer.TryWrite(value);
        public void OnDispatchFailed(LakonaTimerDispatchObservation value, Exception exception)
        {
            Failures.Enqueue(exception);
            completions.Writer.TryWrite(value);
        }
        public void OnDispatchQueued(LakonaTimerDispatchObservation value) { }
        public void OnDispatchQueueFull(LakonaTimerDispatchObservation value) { }
        public void OnDispatchSkipped(LakonaTimerDispatchObservation value) { }
        public void OnStaleHeapEntry(LakonaTimerHeapObservation value) { }
    }
}
