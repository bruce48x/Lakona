using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Timers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class LakonaTimerSchedulerTests : IDisposable
{
    public LakonaTimerSchedulerTests()
    {
        TimerCallbackLog.Reset();
    }

    public void Dispose()
    {
        TimerCallbackLog.Reset();
    }

    [Fact]
    public async Task Due_timers_dispatch_in_due_order()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-30T00:00:00Z"));
        await using var fixture = SchedulerFixture.Create(
            time,
            options: new LakonaTimerOptions { MaxConcurrentCallbacks = 1, DispatchQueueCapacity = 16 });
        await fixture.StartAsync(cancellationToken);

        fixture.Add("third", time.GetUtcNow().AddSeconds(3));
        fixture.Add("first", time.GetUtcNow().AddSeconds(1));
        fixture.Add("second", time.GetUtcNow().AddSeconds(2));

        time.Advance(TimeSpan.FromSeconds(3));
        await TimerCallbackLog.WaitForCountAsync(3, cancellationToken);

        Assert.Equal(["first", "second", "third"], TimerCallbackLog.Values);
    }

    [Fact]
    public async Task Stale_heap_entries_are_skipped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-30T00:00:00Z"));
        await using var fixture = SchedulerFixture.Create(time);
        await fixture.StartAsync(cancellationToken);
        var timerId = fixture.Add("stale", time.GetUtcNow().AddSeconds(1));

        fixture.Destroy(timerId);
        time.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(30, cancellationToken);

        Assert.Empty(TimerCallbackLog.Values);
        Assert.Equal(1, fixture.Observer.StaleHeapEntries);
    }

    [Fact]
    public async Task Destroy_is_idempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-30T00:00:00Z"));
        await using var fixture = SchedulerFixture.Create(time);
        await fixture.StartAsync(cancellationToken);
        var timerId = fixture.Add("destroy", time.GetUtcNow().AddSeconds(1));

        fixture.Destroy(timerId);
        fixture.Destroy(timerId);
        time.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(30, cancellationToken);

        Assert.Empty(TimerCallbackLog.Values);
    }

    [Fact]
    public async Task Destroy_before_lease_prevents_dispatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-30T00:00:00Z"));
        await using var fixture = SchedulerFixture.Create(time);
        var timerId = fixture.Add("destroy-before-lease", time.GetUtcNow().AddSeconds(1));

        fixture.Destroy(timerId);
        await fixture.StartAsync(cancellationToken);
        time.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(30, cancellationToken);

        Assert.Empty(TimerCallbackLog.Values);
    }

    [Fact]
    public async Task Destroy_after_worker_starts_before_lease_prevents_callback_entry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-30T00:00:00Z"));
        await using var fixture = SchedulerFixture.Create(
            time,
            runtimeAccessorFactory: snapshot => new BlockingRuntimeAccessor(snapshot));
        await fixture.StartAsync(cancellationToken);
        var timerId = fixture.Add("destroy-before-lease", time.GetUtcNow().AddSeconds(1));
        var runtimeAccessor = (BlockingRuntimeAccessor)fixture.RuntimeAccessor;

        time.Advance(TimeSpan.FromSeconds(1));
        try
        {
            await runtimeAccessor.WaitForAcquireStartedAsync(cancellationToken);
            fixture.Destroy(timerId);
            runtimeAccessor.ReleaseAcquire();
            await Task.Delay(50, cancellationToken);
        }
        finally
        {
            runtimeAccessor.ReleaseAcquire();
        }

        Assert.Empty(TimerCallbackLog.Values);
    }

    [Fact]
    public async Task Destroy_while_queued_prevents_callback_entry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-30T00:00:00Z"));
        await using var fixture = SchedulerFixture.Create(
            time,
            options: new LakonaTimerOptions { MaxConcurrentCallbacks = 1, DispatchQueueCapacity = 8 });
        await fixture.StartAsync(cancellationToken);
        TimerCallbackLog.BlockValue = "running";

        fixture.Add("running", time.GetUtcNow().AddSeconds(1));
        var queued = fixture.Add("queued", time.GetUtcNow().AddSeconds(1));
        time.Advance(TimeSpan.FromSeconds(1));
        await TimerCallbackLog.WaitForValueAsync("running", cancellationToken);

        fixture.Destroy(queued);
        TimerCallbackLog.ReleaseBlocked();
        await Task.Delay(50, cancellationToken);

        Assert.DoesNotContain("queued", TimerCallbackLog.Values);
    }

    [Fact]
    public async Task Destroy_while_running_cancels_callback_token_and_removes_periodic_follow_up()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-30T00:00:00Z"));
        await using var fixture = SchedulerFixture.Create(time);
        await fixture.StartAsync(cancellationToken);
        TimerCallbackLog.BlockValue = "running";
        var timerId = fixture.Add("running", time.GetUtcNow().AddSeconds(1), TimeSpan.FromSeconds(1));

        time.Advance(TimeSpan.FromSeconds(1));
        await TimerCallbackLog.WaitForValueAsync("running", cancellationToken);
        fixture.Destroy(timerId);

        await TimerCallbackLog.WaitForCancellationAsync(cancellationToken);
        TimerCallbackLog.ReleaseBlocked();
        time.Advance(TimeSpan.FromSeconds(3));
        await Task.Delay(50, cancellationToken);

        Assert.Equal(["running"], TimerCallbackLog.Values);
    }

    [Fact]
    public async Task One_shot_timer_expires_after_successful_dispatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-30T00:00:00Z"));
        await using var fixture = SchedulerFixture.Create(time);
        await fixture.StartAsync(cancellationToken);
        var timerId = fixture.Add("once", time.GetUtcNow().AddSeconds(1));

        time.Advance(TimeSpan.FromSeconds(1));
        await TimerCallbackLog.WaitForCountAsync(1, cancellationToken);

        Assert.False(fixture.Contains(timerId));
    }

    [Fact]
    public async Task Periodic_timer_reschedules_after_callback_completes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-30T00:00:00Z"));
        await using var fixture = SchedulerFixture.Create(time);
        await fixture.StartAsync(cancellationToken);
        fixture.Add("periodic", time.GetUtcNow().AddSeconds(1), TimeSpan.FromSeconds(1));

        time.Advance(TimeSpan.FromSeconds(1));
        await TimerCallbackLog.WaitForCountAsync(1, cancellationToken);
        time.Advance(TimeSpan.FromSeconds(1));
        await TimerCallbackLog.WaitForCountAsync(2, cancellationToken);

        Assert.Equal(["periodic", "periodic"], TimerCallbackLog.Values);
    }

    [Fact]
    public async Task Periodic_timer_does_not_create_catch_up_storm_while_pending()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-30T00:00:00Z"));
        await using var fixture = SchedulerFixture.Create(time);
        await fixture.StartAsync(cancellationToken);
        TimerCallbackLog.BlockValue = "slow";
        fixture.Add("slow", time.GetUtcNow().AddSeconds(1), TimeSpan.FromSeconds(1));

        time.Advance(TimeSpan.FromSeconds(1));
        await TimerCallbackLog.WaitForValueAsync("slow", cancellationToken);
        time.Advance(TimeSpan.FromSeconds(10));
        await Task.Delay(30, cancellationToken);
        TimerCallbackLog.ReleaseBlocked();
        await Task.Delay(30, cancellationToken);

        Assert.Equal(["slow"], TimerCallbackLog.Values);
        Assert.True(fixture.Observer.SkippedDueSlots >= 1);
    }

    [Fact]
    public async Task Periodic_pending_skip_does_not_stale_queued_callback()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-30T00:00:00Z"));
        await using var fixture = SchedulerFixture.Create(
            time,
            options: new LakonaTimerOptions { MaxConcurrentCallbacks = 1, DispatchQueueCapacity = 8 });
        await fixture.StartAsync(cancellationToken);
        TimerCallbackLog.BlockValue = "running";

        fixture.Add("running", time.GetUtcNow().AddSeconds(1));
        fixture.Add("periodic-queued", time.GetUtcNow().AddSeconds(2), TimeSpan.FromSeconds(1));
        time.Advance(TimeSpan.FromSeconds(2));
        await TimerCallbackLog.WaitForValueAsync("running", cancellationToken);
        time.Advance(TimeSpan.FromSeconds(1));
        await fixture.Observer.WaitForSkippedAsync(cancellationToken);

        TimerCallbackLog.ReleaseBlocked();
        await TimerCallbackLog.WaitForValueAsync("periodic-queued", cancellationToken);

        Assert.Contains("periodic-queued", TimerCallbackLog.Values);
        Assert.True(fixture.Observer.SkippedDueSlots >= 1);
    }

    [Fact]
    public async Task Periodic_timer_does_not_dispatch_historical_slots_after_large_time_jump()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-30T00:00:00Z"));
        var observer = new RecordingTimerSchedulerObserver { BlockQueued = true };
        await using var fixture = SchedulerFixture.Create(time, observer: observer);
        await fixture.StartAsync(cancellationToken);
        fixture.Add("jump", time.GetUtcNow().AddSeconds(1), TimeSpan.FromSeconds(1));

        time.Advance(TimeSpan.FromSeconds(100));
        try
        {
            await observer.WaitForQueuedAsync(cancellationToken);
            await TimerCallbackLog.WaitForValueAsync("jump", cancellationToken);
            observer.ReleaseQueued();
            await Task.Delay(50, cancellationToken);
        }
        finally
        {
            observer.ReleaseQueued();
        }

        Assert.Equal(["jump"], TimerCallbackLog.Values);
    }

    [Fact]
    public async Task Periodic_queue_full_reports_skipped_due_work_with_period_metadata()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var period = TimeSpan.FromSeconds(5);
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-30T00:00:00Z"));
        await using var fixture = SchedulerFixture.Create(
            time,
            options: new LakonaTimerOptions { MaxConcurrentCallbacks = 1, DispatchQueueCapacity = 1 });
        await fixture.StartAsync(cancellationToken);
        TimerCallbackLog.BlockValue = "running";

        fixture.Add("running", time.GetUtcNow().AddSeconds(1));
        fixture.Add("queued", time.GetUtcNow().AddSeconds(2));
        fixture.Add("periodic-full", time.GetUtcNow().AddSeconds(3), period);
        time.Advance(TimeSpan.FromSeconds(1));
        await TimerCallbackLog.WaitForValueAsync("running", cancellationToken);
        time.Advance(TimeSpan.FromSeconds(2));
        await fixture.Observer.WaitForQueueFullAsync(cancellationToken);

        TimerCallbackLog.ReleaseBlocked();

        var queueFull = Assert.Single(fixture.Observer.QueueFull);
        var skipped = Assert.Single(fixture.Observer.Skipped);
        Assert.Equal(period, queueFull.Period);
        Assert.Equal(period, skipped.Period);
    }

    [Fact]
    public async Task Timer_callback_can_create_timer_inside_active_execution_scope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-30T00:00:00Z"));
        await using var fixture = SchedulerFixture.Create(time);
        await fixture.StartAsync(cancellationToken);
        fixture.Add("create-child", time.GetUtcNow().AddSeconds(1));

        time.Advance(TimeSpan.FromSeconds(1));
        await TimerCallbackLog.WaitForValueAsync("create-child", cancellationToken);
        time.Advance(TimeSpan.FromSeconds(1));
        await TimerCallbackLog.WaitForValueAsync("child", cancellationToken);

        Assert.Equal(["create-child", "child"], TimerCallbackLog.Values);
    }

    [Fact]
    public async Task Scheduler_backed_backend_uses_injected_time_provider_for_due_time()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2000-01-01T00:00:00Z"));
        await using var fixture = SchedulerFixture.Create(time);
        await fixture.StartAsync(cancellationToken);
        using var lease = fixture.RuntimeAccessor.AcquireCurrent();
        using (LakonaTimerExecutionScope.Enter(fixture.Backend, lease))
        {
            await LakonaTimer.CreateOnceTimerAsync<TimerCallbackTarget, TimerArgs>(
                TimeSpan.FromSeconds(1),
                nameof(TimerCallbackTarget.TickAsync),
                new TimerArgs("from-backend"),
                cancellationToken);
        }

        time.Advance(TimeSpan.FromSeconds(1));
        await TimerCallbackLog.WaitForValueAsync("from-backend", cancellationToken);
    }

    [Fact]
    public async Task Shutdown_cancels_running_callbacks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-30T00:00:00Z"));
        await using var fixture = SchedulerFixture.Create(time);
        await fixture.StartAsync(cancellationToken);
        TimerCallbackLog.BlockValue = "shutdown";
        fixture.Add("shutdown", time.GetUtcNow().AddSeconds(1));

        time.Advance(TimeSpan.FromSeconds(1));
        await TimerCallbackLog.WaitForValueAsync("shutdown", cancellationToken);
        var stopTask = fixture.StopAsync(cancellationToken);

        await TimerCallbackLog.WaitForCancellationAsync(cancellationToken);
        TimerCallbackLog.ReleaseBlocked();
        await stopTask;
    }

    [Fact]
    public async Task Ten_thousand_timers_share_one_scheduler_loop()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-30T00:00:00Z"));
        await using var fixture = SchedulerFixture.Create(
            time,
            options: new LakonaTimerOptions { MaxConcurrentCallbacks = 16, DispatchQueueCapacity = 10_000 });
        await fixture.StartAsync(cancellationToken);

        for (var index = 0; index < 10_000; index++)
        {
            fixture.Add(index.ToString(System.Globalization.CultureInfo.InvariantCulture), time.GetUtcNow().AddSeconds(1));
        }

        time.Advance(TimeSpan.FromSeconds(1));
        await TimerCallbackLog.WaitForCountAsync(10_000, cancellationToken);

        Assert.Equal(1, fixture.SchedulerLoopCount);
        Assert.True(TimerCallbackLog.MaxConcurrent <= 16);
    }

    private sealed class SchedulerFixture : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        private readonly IHotfixRuntimeAccessor runtimeAccessor;
        private readonly LakonaTimerArgsSerializer serializer = new();

        private SchedulerFixture(
            ServiceProvider services,
            IHotfixRuntimeAccessor runtimeAccessor,
            LakonaTimerScheduler scheduler,
            LakonaTimerBackend backend,
            RecordingTimerSchedulerObserver observer)
        {
            this.services = services;
            this.runtimeAccessor = runtimeAccessor;
            Scheduler = scheduler;
            Backend = backend;
            Observer = observer;
        }

        public LakonaTimerScheduler Scheduler { get; }

        public LakonaTimerBackend Backend { get; }

        public RecordingTimerSchedulerObserver Observer { get; }

        public int SchedulerLoopCount => Scheduler.LoopCount;

        public IHotfixRuntimeAccessor RuntimeAccessor => runtimeAccessor;

        public static SchedulerFixture Create(
            ManualTimeProvider time,
            LakonaTimerOptions? options = null,
            Func<HotfixRuntimeSnapshot, IHotfixRuntimeAccessor>? runtimeAccessorFactory = null,
            RecordingTimerSchedulerObserver? observer = null)
        {
            observer ??= new RecordingTimerSchedulerObserver();
            var services = new ServiceCollection().BuildServiceProvider();
            var table = new HotfixDispatchTable(1, Array.Empty<HotfixMethodBinding>());
            var snapshot = new HotfixRuntimeSnapshot(
                new HotfixServiceInvoker(table),
                EmptyHotfixFeatureCommandInvoker.Instance,
                services,
                table,
                services,
                typeof(TimerCallbackTarget).Assembly,
                loadContext: null,
                sourceVersion: null,
                sourceKind: null,
                sourcePath: null,
                ownsRuntimeResources: false,
                onRetired: null);
            var runtimeAccessor = runtimeAccessorFactory?.Invoke(snapshot) ?? new FixedRuntimeAccessor(snapshot);
            var scheduler = new LakonaTimerScheduler(
                runtimeAccessor,
                time,
                options ?? new LakonaTimerOptions { MaxConcurrentCallbacks = 4, DispatchQueueCapacity = 1024 },
                observer,
                NullLogger<LakonaTimerScheduler>.Instance);
            var backend = new LakonaTimerBackend(scheduler);
            return new SchedulerFixture(services, runtimeAccessor, scheduler, backend, observer);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Scheduler.StartAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Scheduler.StopAsync(cancellationToken);
        }

        public TimerId Add(string value, DateTimeOffset dueAtUtc, TimeSpan? period = null)
        {
            var timerId = TimerId.FromGuid(Guid.NewGuid());
            var serialized = serializer.Serialize(new TimerArgs(value));
            Scheduler.Add(new LakonaTimerDescriptor(
                timerId,
                typeof(TimerCallbackTarget).Assembly.GetName().Name!,
                typeof(TimerCallbackTarget).FullName!,
                nameof(TimerCallbackTarget.TickAsync),
                serialized.ArgsAssemblyName,
                serialized.ArgsFullName,
                serialized.SerializerId,
                serialized.JsonPayload,
                dueAtUtc,
                period,
                runtimeAccessor.Current.DispatchTable!.Version));
            return timerId;
        }

        public void Destroy(TimerId timerId)
        {
            Scheduler.Destroy(timerId);
        }

        public bool Contains(TimerId timerId)
        {
            return Scheduler.Contains(timerId);
        }

        public async ValueTask DisposeAsync()
        {
            await Scheduler.DisposeAsync().ConfigureAwait(false);
            await services.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class FixedRuntimeAccessor(HotfixRuntimeSnapshot current) : IHotfixRuntimeAccessor
    {
        public HotfixRuntimeSnapshot Current { get; } = current;

        public HotfixRuntimeSnapshotLease AcquireCurrent()
        {
            return Current.AcquireLease();
        }
    }

    private sealed class BlockingRuntimeAccessor(HotfixRuntimeSnapshot current) : IHotfixRuntimeAccessor
    {
        private readonly TaskCompletionSource acquireStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseAcquire =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HotfixRuntimeSnapshot Current { get; } = current;

        public HotfixRuntimeSnapshotLease AcquireCurrent()
        {
            acquireStarted.TrySetResult();
            releaseAcquire.Task.GetAwaiter().GetResult();
            return Current.AcquireLease();
        }

        public async Task WaitForAcquireStartedAsync(CancellationToken cancellationToken)
        {
            await acquireStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken)
                .ConfigureAwait(false);
        }

        public void ReleaseAcquire()
        {
            releaseAcquire.TrySetResult();
        }
    }

    private sealed class RecordingTimerSchedulerObserver : ILakonaTimerSchedulerObserver
    {
        public int StaleHeapEntries { get; private set; }

        public int SkippedDueSlots { get; private set; }

        public IReadOnlyList<LakonaTimerDispatchObservation> QueueFull
        {
            get
            {
                lock (queueFull)
                {
                    return queueFull.ToArray();
                }
            }
        }

        public IReadOnlyList<LakonaTimerDispatchObservation> Skipped
        {
            get
            {
                lock (skipped)
                {
                    return skipped.ToArray();
                }
            }
        }

        private readonly List<LakonaTimerDispatchObservation> queueFull = [];
        private readonly List<LakonaTimerDispatchObservation> skipped = [];
        private readonly TaskCompletionSource queueFullRecorded =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource skippedRecorded =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource queuedEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseQueued =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool BlockQueued { get; init; }

        public void OnDispatchQueued(LakonaTimerDispatchObservation observation)
        {
            if (!BlockQueued)
            {
                return;
            }

            queuedEntered.TrySetResult();
            releaseQueued.Task.GetAwaiter().GetResult();
        }

        public void OnDispatchQueueFull(LakonaTimerDispatchObservation observation)
        {
            lock (queueFull)
            {
                queueFull.Add(observation);
            }

            queueFullRecorded.TrySetResult();
        }

        public void OnDispatchSkipped(LakonaTimerDispatchObservation observation)
        {
            lock (skipped)
            {
                skipped.Add(observation);
            }

            SkippedDueSlots++;
            skippedRecorded.TrySetResult();
        }

        public void OnDispatchStarted(LakonaTimerDispatchObservation observation)
        {
        }

        public void OnDispatchFailed(LakonaTimerDispatchObservation observation, Exception exception)
        {
        }

        public void OnDispatchCompleted(LakonaTimerDispatchObservation observation)
        {
        }

        public void OnStaleHeapEntry(LakonaTimerHeapObservation observation)
        {
            StaleHeapEntries++;
        }

        public async Task WaitForQueueFullAsync(CancellationToken cancellationToken)
        {
            await queueFullRecorded.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task WaitForQueuedAsync(CancellationToken cancellationToken)
        {
            await queuedEntered.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task WaitForSkippedAsync(CancellationToken cancellationToken)
        {
            await skippedRecorded.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken)
                .ConfigureAwait(false);
        }

        public void ReleaseQueued()
        {
            releaseQueued.TrySetResult();
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ManualTimer> timers = [];
        private DateTimeOffset utcNow = initialUtcNow;

        public override DateTimeOffset GetUtcNow()
        {
            lock (gate)
            {
                return utcNow;
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state, dueTime, period);
            lock (gate)
            {
                timers.Add(timer);
            }

            return timer;
        }

        public void Advance(TimeSpan amount)
        {
            ManualTimer[] due;
            lock (gate)
            {
                utcNow = utcNow.Add(amount);
                due = timers.Where(timer => timer.IsDue(utcNow)).ToArray();
            }

            foreach (var timer in due)
            {
                timer.Fire();
            }
        }

        private void Remove(ManualTimer timer)
        {
            lock (gate)
            {
                timers.Remove(timer);
            }
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly ManualTimeProvider owner;
            private readonly TimerCallback callback;
            private readonly object? state;
            private TimeSpan period;
            private DateTimeOffset dueAtUtc;
            private bool disposed;

            public ManualTimer(
                ManualTimeProvider owner,
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                this.owner = owner;
                this.callback = callback;
                this.state = state;
                this.period = period;
                dueAtUtc = owner.GetUtcNow().Add(dueTime);
            }

            public bool IsDue(DateTimeOffset now)
            {
                return !disposed && now >= dueAtUtc;
            }

            public void Fire()
            {
                if (disposed)
                {
                    return;
                }

                if (period == Timeout.InfiniteTimeSpan)
                {
                    disposed = true;
                    owner.Remove(this);
                }
                else
                {
                    dueAtUtc = owner.GetUtcNow().Add(period);
                }

                ThreadPool.QueueUserWorkItem(_ => callback(state), state, preferLocal: false);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (disposed)
                {
                    return false;
                }

                this.period = period;
                dueAtUtc = owner.GetUtcNow().Add(dueTime);
                return true;
            }

            public void Dispose()
            {
                disposed = true;
                owner.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return default;
            }
        }
    }

    public sealed record TimerArgs(string Value);

    public sealed class TimerCallbackTarget
    {
        public static async ValueTask TickAsync(TimerTick<TimerArgs> tick)
        {
            await TimerCallbackLog.RecordAsync(tick).ConfigureAwait(false);
            if (string.Equals(tick.Args.Value, "create-child", StringComparison.Ordinal))
            {
                await LakonaTimer.CreateOnceTimerAsync<TimerCallbackTarget, TimerArgs>(
                    TimeSpan.FromSeconds(1),
                    nameof(TickAsync),
                    new TimerArgs("child"),
                    tick.CancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static class TimerCallbackLog
    {
        private static readonly object Sync = new();
        private static readonly List<string> ValueList = [];
        private static TaskCompletionSource? releaseBlocked;
        private static TaskCompletionSource? cancellationObserved;
        private static int active;

        public static string? BlockValue { get; set; }

        public static int MaxConcurrent { get; private set; }

        public static IReadOnlyList<string> Values
        {
            get
            {
                lock (Sync)
                {
                    return ValueList.ToArray();
                }
            }
        }

        public static async ValueTask RecordAsync(TimerTick<TimerArgs> tick)
        {
            var currentActive = Interlocked.Increment(ref active);
            lock (Sync)
            {
                MaxConcurrent = Math.Max(MaxConcurrent, currentActive);
                ValueList.Add(tick.Args.Value);
                Monitor.PulseAll(Sync);
            }

            try
            {
                if (string.Equals(BlockValue, tick.Args.Value, StringComparison.Ordinal))
                {
                    TaskCompletionSource release;
                    TaskCompletionSource canceled;
                    lock (Sync)
                    {
                        releaseBlocked ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        cancellationObserved ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        release = releaseBlocked;
                        canceled = cancellationObserved;
                    }

                    using var registration = tick.CancellationToken.Register(static state =>
                        ((TaskCompletionSource)state!).TrySetResult(), canceled);
                    await release.Task.WaitAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }

        public static Task WaitForCountAsync(int count, CancellationToken cancellationToken)
        {
            return WaitUntilAsync(() => ValueList.Count >= count, cancellationToken);
        }

        public static Task WaitForValueAsync(string value, CancellationToken cancellationToken)
        {
            return WaitUntilAsync(() => ValueList.Contains(value, StringComparer.Ordinal), cancellationToken);
        }

        public static async Task WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            TaskCompletionSource source;
            lock (Sync)
            {
                cancellationObserved ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                source = cancellationObserved;
            }

            await source.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }

        public static void ReleaseBlocked()
        {
            lock (Sync)
            {
                releaseBlocked?.TrySetResult();
            }
        }

        public static void Reset()
        {
            lock (Sync)
            {
                ValueList.Clear();
                BlockValue = null;
                releaseBlocked = null;
                cancellationObserved = null;
                MaxConcurrent = 0;
                active = 0;
            }
        }

        private static async Task WaitUntilAsync(Func<bool> predicate, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            while (true)
            {
                lock (Sync)
                {
                    if (predicate())
                    {
                        return;
                    }
                }

                await Task.Delay(5, timeout.Token).ConfigureAwait(false);
            }
        }
    }
}
