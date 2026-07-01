using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Timers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lakona.Game.Server.Tests;

[Collection(TimerTestCollectionNames.TimerRuntime)]
public sealed class LakonaTimerPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Smoke_benchmark_reports_timer_scheduler_metadata()
    {
        var options = TimerBenchmarkOptions.FromEnvironment();

        var results = await TimerBenchmarkRunner.RunAsync(options, TestContext.Current.CancellationToken);

        TimerBenchmarkRunner.WriteReport(results, line =>
        {
            output.WriteLine(line);
            Console.WriteLine(line);
        });
        Assert.All(results.Scenarios, static scenario =>
        {
            Assert.True(scenario.DispatchStarts > 0);
            Assert.Equal(scenario.DispatchStarts, scenario.LatencyObservationCount);
            Assert.Equal(scenario.DispatchStarts / scenario.Options.Duration.TotalSeconds, scenario.ThroughputPerSecond);
            Assert.True(scenario.MaxQueueDepth <= scenario.Options.DispatchQueueCapacity);
            Assert.True(scenario.MaxActiveWorkers <= scenario.Options.MaxConcurrentCallbacks);
            Assert.Equal(0, scenario.HeapStaleEntryCount);
        });
        Assert.Contains("runtime version", results.Report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("benchmark options", results.Report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("timerDescriptorRegistrationLatencyMs", results.Report, StringComparison.Ordinal);
        Assert.DoesNotContain("schedulerRegistrationCreateLatencyMs", results.Report, StringComparison.Ordinal);
    }

    [Fact]
    public void Observer_queue_depth_uses_aggregate_counts_when_started_generation_differs()
    {
        var observer = new BenchmarkTimerSchedulerObserver();
        TimerBenchmarkMeasurementWindow.Start();
        try
        {
            observer.OnDispatchQueued(CreateObservation(generation: 10));
            observer.OnDispatchStarted(CreateObservation(generation: 1));
            observer.OnDispatchQueued(CreateObservation(generation: 11));
        }
        finally
        {
            TimerBenchmarkMeasurementWindow.Stop();
        }

        Assert.Equal(1, observer.MaxQueueDepth);
    }

    [Fact]
    public void Observer_queue_depth_handles_started_before_queued_order()
    {
        var observer = new BenchmarkTimerSchedulerObserver();
        TimerBenchmarkMeasurementWindow.Start();
        try
        {
            observer.OnDispatchStarted(CreateObservation(generation: 1));
            observer.OnDispatchQueued(CreateObservation(generation: 10));
        }
        finally
        {
            TimerBenchmarkMeasurementWindow.Stop();
        }

        Assert.Equal(0, observer.MaxQueueDepth);
    }

    [Theory]
    [InlineData("LAKONA_TIMER_BENCHMARK_TIMER_COUNTS", ",")]
    [InlineData("LAKONA_TIMER_BENCHMARK_TIMER_COUNTS", "1000,,50000")]
    [InlineData("LAKONA_TIMER_BENCHMARK_CALLBACK_COSTS", ",")]
    [InlineData("LAKONA_TIMER_BENCHMARK_CALLBACK_COSTS", "empty,,actor")]
    public void Environment_list_options_reject_empty_entries(string name, string value)
    {
        using var restore = BenchmarkEnvironmentScope.Set(
            ("LAKONA_TIMER_BENCHMARK_SMOKE", "false"),
            (name, value));

        var exception = Assert.Throws<InvalidOperationException>(TimerBenchmarkOptions.FromEnvironment);

        Assert.Contains(name, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Environment_options_reject_periods_longer_than_duration()
    {
        using var restore = BenchmarkEnvironmentScope.Set(
            ("LAKONA_TIMER_BENCHMARK_SMOKE", "false"),
            ("LAKONA_TIMER_BENCHMARK_TIMER_COUNTS", "1"),
            ("LAKONA_TIMER_BENCHMARK_PERIOD_MS", "100"),
            ("LAKONA_TIMER_BENCHMARK_CALLBACK_COSTS", "ACTOR"),
            ("LAKONA_TIMER_BENCHMARK_DURATION_MS", "50"),
            ("LAKONA_TIMER_BENCHMARK_MAX_WORKERS", "1"),
            ("LAKONA_TIMER_BENCHMARK_QUEUE_CAPACITY", "8"));

        var exception = Assert.Throws<InvalidOperationException>(TimerBenchmarkOptions.FromEnvironment);

        Assert.Contains("period", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("duration", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static LakonaTimerDispatchObservation CreateObservation(long generation)
    {
        return new LakonaTimerDispatchObservation(
            TimerId.FromGuid(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            Period: null,
            generation);
    }

    private sealed record TimerBenchmarkOptions(
        bool Smoke,
        IReadOnlyList<int> TimerCounts,
        IReadOnlyList<TimeSpan> Periods,
        IReadOnlyList<TimerCallbackCost> CallbackCosts,
        TimeSpan Duration,
        int MaxConcurrentCallbacks,
        int DispatchQueueCapacity)
    {
        public static TimerBenchmarkOptions FromEnvironment()
        {
            var smoke = GetBool("LAKONA_TIMER_BENCHMARK_SMOKE", defaultValue: true);
            var options = smoke
                ? CreateSmoke()
                : new TimerBenchmarkOptions(
                    Smoke: false,
                    TimerCounts: GetIntList("LAKONA_TIMER_BENCHMARK_TIMER_COUNTS", [1000, 10000, 50000]),
                    Periods: GetMillisecondsList("LAKONA_TIMER_BENCHMARK_PERIOD_MS", [16, 50, 250, 1000]),
                    CallbackCosts: GetCallbackCosts(
                        "LAKONA_TIMER_BENCHMARK_CALLBACK_COSTS",
                        [TimerCallbackCost.Empty, TimerCallbackCost.Actor, TimerCallbackCost.SimulatedRoomBroadcast]),
                    Duration: TimeSpan.FromMilliseconds(GetInt("LAKONA_TIMER_BENCHMARK_DURATION_MS", 2000)),
                    MaxConcurrentCallbacks: GetInt("LAKONA_TIMER_BENCHMARK_MAX_WORKERS", Math.Max(1, Environment.ProcessorCount)),
                    DispatchQueueCapacity: GetInt("LAKONA_TIMER_BENCHMARK_QUEUE_CAPACITY", 65536));
            options.Validate();
            return options;
        }

        private static TimerBenchmarkOptions CreateSmoke()
        {
            return new TimerBenchmarkOptions(
                Smoke: true,
                TimerCounts: [64],
                Periods: [TimeSpan.FromMilliseconds(16)],
                CallbackCosts: [TimerCallbackCost.Empty],
                Duration: TimeSpan.FromMilliseconds(250),
                MaxConcurrentCallbacks: 4,
                DispatchQueueCapacity: 256);
        }

        private static int GetInt(string name, int defaultValue)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value is null)
            {
                return defaultValue;
            }

            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            {
                throw new InvalidOperationException($"{name} must be a positive integer.");
            }

            return parsed;
        }

        private static bool GetBool(string name, bool defaultValue)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value is null)
            {
                return defaultValue;
            }

            if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            throw new InvalidOperationException($"{name} must be true, false, 1, 0, yes, or no.");
        }

        private static IReadOnlyList<int> GetIntList(string name, int[] defaultValue)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value is null)
            {
                return defaultValue;
            }

            var parsed = value.Split(',', StringSplitOptions.TrimEntries)
                .Select(item => ParsePositiveInteger(name, item))
                .ToArray();
            return parsed.Length == 0 ? defaultValue : parsed;
        }

        private static IReadOnlyList<TimeSpan> GetMillisecondsList(string name, int[] defaultValue)
        {
            return GetIntList(name, defaultValue)
                .Select(static milliseconds => TimeSpan.FromMilliseconds(milliseconds))
                .ToArray();
        }

        private static IReadOnlyList<TimerCallbackCost> GetCallbackCosts(string name, TimerCallbackCost[] defaultValue)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value is null)
            {
                return defaultValue;
            }

            var parsed = value.Split(',', StringSplitOptions.TrimEntries)
                .Select(item => ParseCallbackCost(name, item))
                .ToArray();
            return parsed.Length == 0 ? defaultValue : parsed;
        }

        private static int ParsePositiveInteger(string name, string value)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            {
                throw new InvalidOperationException($"{name} values must be positive integers. Invalid value: '{value}'.");
            }

            return parsed;
        }

        private static TimerCallbackCost ParseCallbackCost(string name, string value)
        {
            return value.ToLowerInvariant() switch
            {
                "empty" => TimerCallbackCost.Empty,
                "actor" => TimerCallbackCost.Actor,
                "simulated-room-broadcast" => TimerCallbackCost.SimulatedRoomBroadcast,
                _ => throw new InvalidOperationException($"{name} contains unknown timer callback cost '{value}'.")
            };
        }

        private void Validate()
        {
            var longestPeriod = Periods.Max();
            if (longestPeriod > Duration)
            {
                throw new InvalidOperationException(
                    $"Lakona timer benchmark period {longestPeriod.TotalMilliseconds} ms must be less than or equal to duration {Duration.TotalMilliseconds} ms.");
            }
        }
    }

    public enum TimerCallbackCost
    {
        Empty,
        Actor,
        SimulatedRoomBroadcast
    }

    private static class TimerBenchmarkRunner
    {
        private static readonly LakonaTimerArgsSerializer Serializer = new();

        public static async Task<TimerBenchmarkRun> RunAsync(
            TimerBenchmarkOptions options,
            CancellationToken cancellationToken)
        {
            var scenarios = new List<TimerBenchmarkScenarioResult>();
            foreach (var timerCount in options.TimerCounts)
            {
                foreach (var period in options.Periods)
                {
                    foreach (var callbackCost in options.CallbackCosts)
                    {
                        scenarios.Add(await RunScenarioAsync(
                            new TimerBenchmarkScenarioOptions(
                                timerCount,
                                period,
                                callbackCost,
                                options.Duration,
                                options.MaxConcurrentCallbacks,
                                options.DispatchQueueCapacity),
                            cancellationToken).ConfigureAwait(false));
                    }
                }
            }

            var run = new TimerBenchmarkRun(
                options,
                scenarios,
                RuntimeInformation.FrameworkDescription,
                Environment.OSVersion.VersionString,
                Environment.ProcessorCount,
                GCSettings.IsServerGC ? "server" : "workstation");
            return run with { Report = BuildReport(run) };
        }

        public static void WriteReport(TimerBenchmarkRun run, Action<string> writeLine)
        {
            foreach (var line in run.Report.Split(Environment.NewLine))
            {
                writeLine(line);
            }
        }

        private static async Task<TimerBenchmarkScenarioResult> RunScenarioAsync(
            TimerBenchmarkScenarioOptions options,
            CancellationToken cancellationToken)
        {
            var observer = new BenchmarkTimerSchedulerObserver();
            await using var fixture = BenchmarkSchedulerFixture.Create(options, observer);

            var timerIds = new List<TimerId>(options.TimerCount);
            var createStart = Stopwatch.GetTimestamp();
            for (var index = 0; index < options.TimerCount; index++)
            {
                timerIds.Add(fixture.Add(options.CallbackCost, options.Period));
            }

            var createElapsed = Stopwatch.GetElapsedTime(createStart);
            TimerBenchmarkCallback.Reset();
            observer.ResetMeasuredWindow();
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
            TimerBenchmarkMeasurementWindow.Start();
            try
            {
                await fixture.StartAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(options.Duration, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                TimerBenchmarkMeasurementWindow.Stop();
            }

            var cpuAfter = Process.GetCurrentProcess().TotalProcessorTime;
            var allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
            var measuredObserver = observer.Snapshot();
            var enteredTicks = TimerBenchmarkCallback.EnteredTicks;
            var activeTimerCount = fixture.Scheduler.Descriptors.Count;

            var destroyStart = Stopwatch.GetTimestamp();
            foreach (var timerId in timerIds)
            {
                fixture.Destroy(timerId);
            }

            var destroyElapsed = Stopwatch.GetElapsedTime(destroyStart);
            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);

            return new TimerBenchmarkScenarioResult(
                options,
                DispatchStarts: measuredObserver.DispatchStarts,
                CallbackEnteredTicks: enteredTicks,
                P50DispatchLatency: measuredObserver.GetLatencyPercentile(0.50),
                P95DispatchLatency: measuredObserver.GetLatencyPercentile(0.95),
                P99DispatchLatency: measuredObserver.GetLatencyPercentile(0.99),
                LatencyObservationCount: measuredObserver.LatencyObservationCount,
                LatencySampleCount: measuredObserver.LatencySampleCount,
                ThroughputPerSecond: measuredObserver.DispatchStarts / Math.Max(options.Duration.TotalSeconds, 0.001),
                SkippedTicks: measuredObserver.SkippedTicks,
                CallbackFailures: measuredObserver.CallbackFailures,
                MaxQueueDepth: measuredObserver.MaxQueueDepth,
                QueueFullSkips: measuredObserver.QueueFullSkips,
                MaxActiveWorkers: measuredObserver.MaxActiveWorkers,
                AllocatedBytesPerTick: measuredObserver.DispatchStarts == 0 ? 0 : (allocatedAfter - allocatedBefore) / measuredObserver.DispatchStarts,
                CpuTime: cpuAfter - cpuBefore,
                DescriptorRegistrationLatency: createElapsed,
                DestroyLatency: destroyElapsed,
                ActiveTimerCount: activeTimerCount,
                HeapStaleEntryCount: measuredObserver.HeapStaleEntryCount);
        }

        private static string BuildReport(TimerBenchmarkRun run)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Lakona timer benchmark metadata");
            builder.AppendLine($"runtime version: {run.RuntimeVersion}");
            builder.AppendLine($"OS: {run.OS}");
            builder.AppendLine($"processor count: {run.ProcessorCount}");
            builder.AppendLine($"GC mode: {run.GCMode}");
            builder.AppendLine($"benchmark options: smoke={run.Options.Smoke}; timerCounts={string.Join(",", run.Options.TimerCounts)}; periodsMs={string.Join(",", run.Options.Periods.Select(static period => period.TotalMilliseconds))}; callbackCosts={string.Join(",", run.Options.CallbackCosts.Select(FormatCallbackCost))}; durationMs={run.Options.Duration.TotalMilliseconds}; maxWorkers={run.Options.MaxConcurrentCallbacks}; queueCapacity={run.Options.DispatchQueueCapacity}");
            builder.AppendLine("callback cost model: synthetic benchmark work; names are stable comparison labels, not production actor or network implementations");
            foreach (var scenario in run.Scenarios)
            {
                builder.AppendLine(
                    $"scenario timerCount={scenario.Options.TimerCount} periodMs={scenario.Options.Period.TotalMilliseconds} callbackCost={FormatCallbackCost(scenario.Options.CallbackCost)} " +
                    $"p50DispatchLatencyMsSampledApprox={scenario.P50DispatchLatency.TotalMilliseconds:F3} p95DispatchLatencyMsSampledApprox={scenario.P95DispatchLatency.TotalMilliseconds:F3} p99DispatchLatencyMsSampledApprox={scenario.P99DispatchLatency.TotalMilliseconds:F3} " +
                    $"latencyObservations={scenario.LatencyObservationCount} latencySamples={scenario.LatencySampleCount} " +
                    $"dispatchStarts={scenario.DispatchStarts} callbackEnteredTicks={scenario.CallbackEnteredTicks} throughputDispatchStarts={scenario.ThroughputPerSecond:F2}/s skippedTicks={scenario.SkippedTicks} callbackFailures={scenario.CallbackFailures} maxObservedQueueDepth={scenario.MaxQueueDepth} queueFullSkips={scenario.QueueFullSkips} " +
                    $"activeWorkerCount={scenario.MaxActiveWorkers} allocatedBytesPerTickApproxProcess={scenario.AllocatedBytesPerTick} cpuTimeMsApproxProcess={scenario.CpuTime.TotalMilliseconds:F3} " +
                    $"timerDescriptorRegistrationLatencyMs={scenario.DescriptorRegistrationLatency.TotalMilliseconds:F3} destroyLatencyMs={scenario.DestroyLatency.TotalMilliseconds:F3} activeTimerCount={scenario.ActiveTimerCount} heapStaleEntryCount={scenario.HeapStaleEntryCount}");
            }

            return builder.ToString();
        }

        private static string FormatCallbackCost(TimerCallbackCost cost)
        {
            return cost switch
            {
                TimerCallbackCost.Empty => "empty",
                TimerCallbackCost.Actor => "actor",
                TimerCallbackCost.SimulatedRoomBroadcast => "simulated-room-broadcast",
                _ => cost.ToString()
            };
        }

        private sealed class BenchmarkSchedulerFixture : IAsyncDisposable
        {
            private readonly ServiceProvider services;
            private readonly FixedRuntimeAccessor runtimeAccessor;

            private BenchmarkSchedulerFixture(
                ServiceProvider services,
                FixedRuntimeAccessor runtimeAccessor,
                LakonaTimerScheduler scheduler)
            {
                this.services = services;
                this.runtimeAccessor = runtimeAccessor;
                Scheduler = scheduler;
            }

            public LakonaTimerScheduler Scheduler { get; }

            public static BenchmarkSchedulerFixture Create(
                TimerBenchmarkScenarioOptions options,
                ILakonaTimerSchedulerObserver observer)
            {
                var services = new ServiceCollection().BuildServiceProvider();
                var table = new HotfixDispatchTable(1, Array.Empty<HotfixMethodBinding>());
                var snapshot = new HotfixRuntimeSnapshot(
                    new HotfixServiceInvoker(table),
                    EmptyHotfixFeatureCommandInvoker.Instance,
                    services,
                    table,
                    services,
                    typeof(TimerBenchmarkCallback).Assembly,
                    loadContext: null,
                    sourceVersion: null,
                    sourcePath: null,
                    ownsRuntimeResources: false,
                    onRetired: null);
                var runtimeAccessor = new FixedRuntimeAccessor(snapshot);
                var scheduler = new LakonaTimerScheduler(
                    runtimeAccessor,
                    TimeProvider.System,
                    new LakonaTimerOptions
                    {
                        MaxConcurrentCallbacks = options.MaxConcurrentCallbacks,
                        DispatchQueueCapacity = options.DispatchQueueCapacity
                    },
                    observer,
                    NullLogger<LakonaTimerScheduler>.Instance);
                _ = new LakonaTimerBackend(scheduler);
                return new BenchmarkSchedulerFixture(services, runtimeAccessor, scheduler);
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                return Scheduler.StartAsync(cancellationToken);
            }

            public TimerId Add(TimerCallbackCost callbackCost, TimeSpan period)
            {
                var timerId = TimerId.FromGuid(Guid.NewGuid());
                var serialized = Serializer.Serialize(new TimerBenchmarkArgs(callbackCost));
                Scheduler.Add(new LakonaTimerDescriptor(
                    timerId,
                    typeof(TimerBenchmarkCallback).Assembly.GetName().Name!,
                    typeof(TimerBenchmarkCallback).FullName!,
                    nameof(TimerBenchmarkCallback.TickAsync),
                    serialized.ArgsAssemblyName,
                    serialized.ArgsFullName,
                    serialized.SerializerId,
                    serialized.JsonPayload,
                    DateTimeOffset.UtcNow.Add(period),
                    period,
                    runtimeAccessor.Current.DispatchTable!.Version));
                return timerId;
            }

            public void Destroy(TimerId timerId)
            {
                Scheduler.Destroy(timerId);
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
    }

    private sealed class BenchmarkTimerSchedulerObserver : ILakonaTimerSchedulerObserver
    {
        private readonly object gate = new();
        private readonly BoundedLatencySampler latencySampler = new(8192);
        private int activeWorkers;
        private int maxQueueDepth;
        private int maxActiveWorkers;
        private long queuedObservations;
        private long startedObservations;
        private long skippedTicks;
        private long callbackFailures;
        private long queueFullSkips;
        private long heapStaleEntryCount;

        public long SkippedTicks => Volatile.Read(ref skippedTicks);

        public long CallbackFailures => Volatile.Read(ref callbackFailures);

        public int MaxQueueDepth => Volatile.Read(ref maxQueueDepth);

        public long QueueFullSkips => Volatile.Read(ref queueFullSkips);

        public int MaxActiveWorkers => Volatile.Read(ref maxActiveWorkers);

        public long HeapStaleEntryCount => Volatile.Read(ref heapStaleEntryCount);

        public long LatencyObservationCount => latencySampler.ObservationCount;

        public int LatencySampleCount => latencySampler.SampleCount;

        public void ResetMeasuredWindow()
        {
            lock (gate)
            {
                queuedObservations = 0;
                startedObservations = 0;
                maxQueueDepth = 0;
            }

            Volatile.Write(ref activeWorkers, 0);
            Volatile.Write(ref maxActiveWorkers, 0);
            Volatile.Write(ref skippedTicks, 0);
            Volatile.Write(ref callbackFailures, 0);
            Volatile.Write(ref queueFullSkips, 0);
            Volatile.Write(ref heapStaleEntryCount, 0);
            latencySampler.Reset();
        }

        public BenchmarkTimerObserverSnapshot Snapshot()
        {
            int queueDepthSnapshot;
            lock (gate)
            {
                UpdateMax(ref maxQueueDepth, GetObservedQueueDepth());
                queueDepthSnapshot = maxQueueDepth;
            }

            return new BenchmarkTimerObserverSnapshot(
                startedObservations,
                SkippedTicks,
                CallbackFailures,
                queueDepthSnapshot,
                QueueFullSkips,
                MaxActiveWorkers,
                HeapStaleEntryCount,
                latencySampler.Snapshot());
        }

        public void OnDispatchQueued(LakonaTimerDispatchObservation observation)
        {
            if (!TimerBenchmarkMeasurementWindow.IsActive)
            {
                return;
            }

            lock (gate)
            {
                queuedObservations++;
                UpdateMax(ref maxQueueDepth, GetObservedQueueDepth());
            }
        }

        public void OnDispatchQueueFull(LakonaTimerDispatchObservation observation)
        {
            if (!TimerBenchmarkMeasurementWindow.IsActive)
            {
                return;
            }

            Interlocked.Increment(ref queueFullSkips);
        }

        public void OnDispatchSkipped(LakonaTimerDispatchObservation observation)
        {
            if (!TimerBenchmarkMeasurementWindow.IsActive)
            {
                return;
            }

            Interlocked.Increment(ref skippedTicks);
        }

        public void OnDispatchStarted(LakonaTimerDispatchObservation observation)
        {
            if (!TimerBenchmarkMeasurementWindow.IsActive)
            {
                return;
            }

            lock (gate)
            {
                startedObservations++;
                UpdateMax(ref maxQueueDepth, GetObservedQueueDepth());
            }

            var current = Interlocked.Increment(ref activeWorkers);
            UpdateMax(ref maxActiveWorkers, current);
            latencySampler.Add(Math.Max(0, (DateTimeOffset.UtcNow - observation.DueAtUtc).TotalMilliseconds));
        }

        public void OnDispatchFailed(LakonaTimerDispatchObservation observation, Exception exception)
        {
            if (!TimerBenchmarkMeasurementWindow.IsActive)
            {
                return;
            }

            Interlocked.Increment(ref callbackFailures);
            Interlocked.Decrement(ref activeWorkers);
        }

        public void OnDispatchCompleted(LakonaTimerDispatchObservation observation)
        {
            if (!TimerBenchmarkMeasurementWindow.IsActive)
            {
                return;
            }

            Interlocked.Decrement(ref activeWorkers);
        }

        public void OnStaleHeapEntry(LakonaTimerHeapObservation observation)
        {
            if (!TimerBenchmarkMeasurementWindow.IsActive)
            {
                return;
            }

            Interlocked.Increment(ref heapStaleEntryCount);
        }

        public TimeSpan GetLatencyPercentile(double percentile)
        {
            var values = latencySampler.GetSamples();
            if (values.Length == 0)
            {
                return TimeSpan.Zero;
            }

            Array.Sort(values);
            var index = Math.Clamp((int)Math.Ceiling(values.Length * percentile) - 1, 0, values.Length - 1);
            return TimeSpan.FromMilliseconds(values[index]);
        }

        private int GetObservedQueueDepth()
        {
            return (int)Math.Min(int.MaxValue, Math.Max(0, queuedObservations - startedObservations));
        }

        private static void UpdateMax(ref int target, int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref target);
                if (value <= current || Interlocked.CompareExchange(ref target, value, current) == current)
                {
                    return;
                }
            }
        }
    }

    private sealed record BenchmarkTimerObserverSnapshot(
        long DispatchStarts,
        long SkippedTicks,
        long CallbackFailures,
        int MaxQueueDepth,
        long QueueFullSkips,
        int MaxActiveWorkers,
        long HeapStaleEntryCount,
        BoundedLatencySnapshot Latency)
    {
        public long LatencyObservationCount => Latency.ObservationCount;

        public int LatencySampleCount => Latency.SampleCount;

        public TimeSpan GetLatencyPercentile(double percentile)
        {
            return Latency.GetPercentile(percentile);
        }
    }

    private sealed class BoundedLatencySampler(int capacity)
    {
        private readonly object gate = new();
        private readonly double[] samples = new double[capacity];
        private long observations;
        private int count;

        public long ObservationCount => Volatile.Read(ref observations);

        public int SampleCount
        {
            get
            {
                lock (gate)
                {
                    return count;
                }
            }
        }

        public void Add(double latencyMilliseconds)
        {
            lock (gate)
            {
                var observed = ++observations;
                if (count < samples.Length)
                {
                    samples[count++] = latencyMilliseconds;
                    return;
                }

                var replacementIndex = GetReservoirReplacementIndex(observed);
                if (replacementIndex < (ulong)samples.Length)
                {
                    samples[(int)replacementIndex] = latencyMilliseconds;
                }
            }
        }

        public void Reset()
        {
            lock (gate)
            {
                observations = 0;
                count = 0;
            }
        }

        public BoundedLatencySnapshot Snapshot()
        {
            lock (gate)
            {
                return new BoundedLatencySnapshot(observations, samples.Take(count).ToArray());
            }
        }

        public double[] GetSamples()
        {
            lock (gate)
            {
                return samples.Take(count).ToArray();
            }
        }

        private static ulong GetReservoirReplacementIndex(long observed)
        {
            var mixed = unchecked((ulong)observed * 11400714819323198485UL);
            return mixed % (ulong)observed;
        }
    }

    private sealed record BoundedLatencySnapshot(long ObservationCount, double[] Samples)
    {
        public int SampleCount => Samples.Length;

        public TimeSpan GetPercentile(double percentile)
        {
            if (Samples.Length == 0)
            {
                return TimeSpan.Zero;
            }

            var values = Samples.ToArray();
            Array.Sort(values);
            var index = Math.Clamp((int)Math.Ceiling(values.Length * percentile) - 1, 0, values.Length - 1);
            return TimeSpan.FromMilliseconds(values[index]);
        }
    }

    private sealed record TimerBenchmarkRun(
        TimerBenchmarkOptions Options,
        IReadOnlyList<TimerBenchmarkScenarioResult> Scenarios,
        string RuntimeVersion,
        string OS,
        int ProcessorCount,
        string GCMode)
    {
        public string Report { get; init; } = string.Empty;
    }

    private sealed record TimerBenchmarkScenarioOptions(
        int TimerCount,
        TimeSpan Period,
        TimerCallbackCost CallbackCost,
        TimeSpan Duration,
        int MaxConcurrentCallbacks,
        int DispatchQueueCapacity);

    private sealed record TimerBenchmarkScenarioResult(
        TimerBenchmarkScenarioOptions Options,
        long DispatchStarts,
        long CallbackEnteredTicks,
        TimeSpan P50DispatchLatency,
        TimeSpan P95DispatchLatency,
        TimeSpan P99DispatchLatency,
        long LatencyObservationCount,
        int LatencySampleCount,
        double ThroughputPerSecond,
        long SkippedTicks,
        long CallbackFailures,
        int MaxQueueDepth,
        long QueueFullSkips,
        int MaxActiveWorkers,
        long AllocatedBytesPerTick,
        TimeSpan CpuTime,
        TimeSpan DescriptorRegistrationLatency,
        TimeSpan DestroyLatency,
        int ActiveTimerCount,
        long HeapStaleEntryCount);

    private sealed class BenchmarkEnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> previousValues;

        private BenchmarkEnvironmentScope(Dictionary<string, string?> previousValues)
        {
            this.previousValues = previousValues;
        }

        public static BenchmarkEnvironmentScope Set(params (string Name, string Value)[] values)
        {
            var previousValues = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var (name, value) in values)
            {
                previousValues.TryAdd(name, Environment.GetEnvironmentVariable(name));
                Environment.SetEnvironmentVariable(name, value);
            }

            return new BenchmarkEnvironmentScope(previousValues);
        }

        public void Dispose()
        {
            foreach (var (name, value) in previousValues)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    private static class TimerBenchmarkMeasurementWindow
    {
        private static int active;

        public static bool IsActive => Volatile.Read(ref active) != 0;

        public static void Start()
        {
            Volatile.Write(ref active, 1);
        }

        public static void Stop()
        {
            Volatile.Write(ref active, 0);
        }
    }

    public sealed record TimerBenchmarkArgs(TimerCallbackCost Cost);

    public sealed class TimerBenchmarkCallback
    {
        private const int ActorShardCount = 64;
        private static readonly object[] ActorGates = Enumerable.Range(0, ActorShardCount).Select(static _ => new object()).ToArray();
        private static readonly long[] ActorState = new long[ActorShardCount];
        private static long enteredTicks;
        private static long broadcastChecksum;

        public static long EnteredTicks => Volatile.Read(ref enteredTicks);

        public static ValueTask TickAsync(TimerTick<TimerBenchmarkArgs> tick)
        {
            if (!TimerBenchmarkMeasurementWindow.IsActive)
            {
                return default;
            }

            var currentTick = Interlocked.Increment(ref enteredTicks);
            switch (tick.Args.Cost)
            {
                case TimerCallbackCost.Empty:
                    break;
                case TimerCallbackCost.Actor:
                    RunSyntheticActorCost(tick.TimerId, currentTick);
                    break;
                case TimerCallbackCost.SimulatedRoomBroadcast:
                    RunSyntheticRoomBroadcastCost(currentTick);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported callback cost '{tick.Args.Cost}'.");
            }

            return default;
        }

        public static void Reset()
        {
            Volatile.Write(ref enteredTicks, 0);
            Volatile.Write(ref broadcastChecksum, 0);
            Array.Clear(ActorState, 0, ActorState.Length);
        }

        private static void RunSyntheticActorCost(TimerId timerId, long currentTick)
        {
            var shard = (timerId.GetHashCode() & int.MaxValue) % ActorShardCount;
            lock (ActorGates[shard])
            {
                var state = ActorState[shard];
                for (var step = 0; step < 8; step++)
                {
                    state = long.RotateLeft(state ^ currentTick ^ step, 7) + 0x9E3779B9;
                }

                ActorState[shard] = state;
            }
        }

        private static void RunSyntheticRoomBroadcastCost(long currentTick)
        {
            var rented = ArrayPool<byte>.Shared.Rent(512);
            try
            {
                rented.AsSpan(0, 512).Fill((byte)(currentTick & 0xFF));
                long checksum = 0;
                for (var recipient = 0; recipient < 32; recipient++)
                {
                    var recipientSalt = recipient * 31;
                    for (var index = 0; index < 512; index += 32)
                    {
                        checksum += rented[index] ^ recipientSalt;
                    }
                }

                Interlocked.Add(ref broadcastChecksum, checksum);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}
