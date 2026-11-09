using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Timers;
using Microsoft.Extensions.Logging.Abstractions;

var results = new List<object>();
foreach (var baseline in new[] { true, false })
{
    await MeasureAsync(baseline, burst: false, count: 32, round: -1);
    await MeasureAsync(baseline, burst: true, count: 64, round: -1);
}
for (var round = 0; round < 5; round++)
{
    foreach (var burst in new[] { false, true })
    foreach (var baseline in round % 2 == 0 ? new[] { true, false } : new[] { false, true })
    {
        var result = await MeasureAsync(baseline, burst, burst ? 10_000 : 128, round);
        results.Add(result);
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
}
File.WriteAllText(args[0], JsonSerializer.Serialize(new
{
    environment = new
    {
        runtime = RuntimeInformation.FrameworkDescription,
        os = RuntimeInformation.OSDescription,
        logicalProcessors = Environment.ProcessorCount,
        serverGC = GCSettings.IsServerGC,
        tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
        configuration = "Release", registrationProducers = 1,
        ordinaryDueMilliseconds = 2, burstProbes = 16,
        warmupOrdinary = 32, warmupBurst = 64
    },
    results
}, new JsonSerializerOptions { WriteIndented = true }));

static async Task<object> MeasureAsync(bool baseline, bool burst, int count, int round)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var ct = timeout.Token;
    using var clock = new CountingClock(burst);
    var observer = new RecordingObserver();
    // Isolate the scheduler's dispatch boundary. Business/Hotfix callbacks are not
    // invoked; the same no-op Actor owner accepts work in both implementations.
    var owner = new ActorTimerOwner(static _ => default, static () => true);
    var options = new LakonaTimerOptions();
    Action<LakonaTimerDescriptor> add;
    Func<Task> start;
    Func<Task> stop;
    IAsyncDisposable scheduler;
    if (baseline)
    {
        var instance = new BaselineTimerScheduler(null, clock, options, observer, NullLogger<BaselineTimerScheduler>.Instance);
        scheduler = instance; add = instance.Add;
        start = () => instance.StartAsync(ct); stop = () => instance.StopAsync(ct);
    }
    else
    {
        var instance = new LakonaTimerScheduler(null, clock, options, observer, NullLogger<LakonaTimerScheduler>.Instance);
        scheduler = instance; add = instance.Add;
        start = () => instance.StartAsync(ct); stop = () => instance.StopAsync(ct);
    }
    await using (scheduler)
    {
        await start();
        try
        {
            var latencies = new List<double>(count);
            long releaseTimestamp = 0;
            long postBurstAllocatedBefore = 0;
            long registrationTicks = 0;
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var timestamp = Stopwatch.GetTimestamp();
            if (burst)
            {
                var due = DateTimeOffset.UtcNow.AddHours(1);
                add(Descriptor(due));
                await clock.FirstArm.Task.WaitAsync(ct);
                for (var index = 0; index < count; index++)
                {
                    var descriptor = Descriptor(due.AddHours(1));
                    var before = Stopwatch.GetTimestamp();
                    add(descriptor);
                    registrationTicks += Stopwatch.GetTimestamp() - before;
                }
                var probes = new List<Task>();
                for (var index = 0; index < 16; index++)
                {
                    var descriptor = Descriptor(DateTimeOffset.UtcNow);
                    probes.Add(observer.Expect(descriptor.TimerId));
                    add(descriptor);
                }
                postBurstAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
                releaseTimestamp = Stopwatch.GetTimestamp();
                clock.Release.Set();
                await Task.WhenAll(probes).WaitAsync(ct);
                await clock.WaitForArmingToSettleAsync(ct);
            }
            else
            {
                for (var index = 0; index < count; index++)
                {
                    var descriptor = Descriptor(DateTimeOffset.UtcNow.AddMilliseconds(2));
                    var completed = observer.Expect(descriptor.TimerId);
                    var before = Stopwatch.GetTimestamp();
                    add(descriptor);
                    var ticks = Stopwatch.GetTimestamp() - before;
                    registrationTicks += ticks;
                    latencies.Add(ticks * 1_000_000d / Stopwatch.Frequency);
                    await completed.WaitAsync(ct);
                }
            }
            var elapsed = Stopwatch.GetElapsedTime(timestamp);
            var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            var postBurstAllocated = burst ? GC.GetTotalAllocatedBytes(precise: true) - postBurstAllocatedBefore : 0;
            var drain = burst ? Math.Max(0, Stopwatch.GetElapsedTime(releaseTimestamp, clock.LastArmTimestamp).TotalMilliseconds) : 0;
            var arms = clock.Arms;
            await stop();
            if (clock.ActiveTimers != 0 || observer.Failed != 0 || observer.Completed != (burst ? 16 : count))
                throw new InvalidOperationException("Scheduler failed, lost a probe, or retained timers after stop.");
            var triggered = observer.Latencies.ToArray();
            Array.Sort(triggered);
            latencies.Sort();
            return new
            {
                implementation = baseline ? "baseline" : "current", burst, count, round,
                registrationMeanMicroseconds = registrationTicks * 1_000_000d / Stopwatch.Frequency / count,
                registrationP95Microseconds = Percentile(latencies.ToArray(), .95),
                elapsedMilliseconds = elapsed.TotalMilliseconds,
                allocatedBytes = allocated, delayArms = arms,
                postBurstLastArmMilliseconds = drain, postBurstAllocatedBytes = postBurstAllocated,
                triggerP50Milliseconds = Percentile(triggered, .50),
                triggerP95Milliseconds = Percentile(triggered, .95),
                triggerP99Milliseconds = Percentile(triggered, .99),
                triggerSamples = triggered, activeTimersAfterStop = clock.ActiveTimers
            };
        }
        finally { clock.Release.Set(); }
    }

    LakonaTimerDescriptor Descriptor(DateTimeOffset due) => new(
        TimerId.FromGuid(Guid.NewGuid()), "probe", "probe", "probe", 1,
        "none", "none", "none", ReadOnlyMemory<byte>.Empty, due, null, 1) { Owner = owner };
}

static double Percentile(double[] sorted, double percentile) =>
    sorted.Length == 0 ? 0 : sorted[(int)Math.Ceiling(sorted.Length * percentile) - 1];

sealed class RecordingObserver : ILakonaTimerSchedulerObserver
{
    private readonly ConcurrentDictionary<TimerId, TaskCompletionSource> expected = new();
    public ConcurrentQueue<double> Latencies { get; } = new();
    public int Failed;
    public int Completed;
    public Task Expect(TimerId id) => expected.GetOrAdd(id, static _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
    public void OnDispatchStarted(LakonaTimerDispatchObservation value) =>
        Latencies.Enqueue(Math.Max(0, (DateTimeOffset.UtcNow - value.DueAtUtc).TotalMilliseconds));
    public void OnDispatchCompleted(LakonaTimerDispatchObservation value)
    {
        Interlocked.Increment(ref Completed);
        if (expected.TryRemove(value.TimerId, out var completion)) completion.TrySetResult();
    }
    public void OnDispatchFailed(LakonaTimerDispatchObservation value, Exception exception) => Interlocked.Increment(ref Failed);
    public void OnDispatchQueued(LakonaTimerDispatchObservation value) { }
    public void OnDispatchQueueFull(LakonaTimerDispatchObservation value) { }
    public void OnDispatchSkipped(LakonaTimerDispatchObservation value) { }
    public void OnStaleHeapEntry(LakonaTimerHeapObservation value) { }
}

sealed class CountingClock(bool blockFirst) : TimeProvider, IDisposable
{
    private int arms;
    private int active;
    private long lastArmTimestamp;
    public int Arms => Volatile.Read(ref arms);
    public int ActiveTimers => Volatile.Read(ref active);
    public long LastArmTimestamp => Volatile.Read(ref lastArmTimestamp);
    public TaskCompletionSource FirstArm { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ManualResetEventSlim Release { get; } = new(false);
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var number = Interlocked.Increment(ref arms);
        Volatile.Write(ref lastArmTimestamp, Stopwatch.GetTimestamp());
        if (number == 1 && blockFirst)
        {
            FirstArm.TrySetResult();
            if (!Release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("First timer arm was not released.");
        }
        Interlocked.Increment(ref active);
        return new TrackedTimer(System.CreateTimer(callback, state, dueTime, period), this);
    }
    public async Task WaitForArmingToSettleAsync(CancellationToken ct)
    {
        int previous;
        do { previous = Arms; await Task.Delay(50, ct); } while (previous != Arms);
    }
    public void Dispose() => Release.Dispose();
    private sealed class TrackedTimer(ITimer timer, CountingClock owner) : ITimer
    {
        private int disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period) => timer.Change(dueTime, period);
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            timer.Dispose();
            Interlocked.Decrement(ref owner.active);
        }
        public ValueTask DisposeAsync() { Dispose(); return default; }
    }
}
