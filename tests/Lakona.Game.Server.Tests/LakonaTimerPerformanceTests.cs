using System.Collections.Concurrent;
using System.Diagnostics;
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
            Assert.True(scenario.EnteredTicks > 0);
            Assert.True(scenario.MaxQueueDepth <= scenario.Options.DispatchQueueCapacity);
            Assert.True(scenario.MaxActiveWorkers <= scenario.Options.MaxConcurrentCallbacks);
        });
        Assert.Contains("runtime version", results.Report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("benchmark options", results.Report, StringComparison.OrdinalIgnoreCase);
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
            return smoke
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
            return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : defaultValue;
        }

        private static bool GetBool(string name, bool defaultValue)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private static IReadOnlyList<int> GetIntList(string name, int[] defaultValue)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            var parsed = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static item => int.Parse(item, System.Globalization.CultureInfo.InvariantCulture))
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
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            var parsed = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ParseCallbackCost)
                .ToArray();
            return parsed.Length == 0 ? defaultValue : parsed;
        }

        private static TimerCallbackCost ParseCallbackCost(string value)
        {
            return value switch
            {
                "empty" => TimerCallbackCost.Empty,
                "actor" => TimerCallbackCost.Actor,
                "simulated-room-broadcast" => TimerCallbackCost.SimulatedRoomBroadcast,
                _ => throw new InvalidOperationException($"Unknown timer callback cost '{value}'.")
            };
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
            TimerBenchmarkCallback.Reset();
            var observer = new BenchmarkTimerSchedulerObserver();
            await using var fixture = BenchmarkSchedulerFixture.Create(options, observer);
            await fixture.StartAsync(cancellationToken).ConfigureAwait(false);

            var timerIds = new List<TimerId>(options.TimerCount);
            var createStart = Stopwatch.GetTimestamp();
            for (var index = 0; index < options.TimerCount; index++)
            {
                timerIds.Add(fixture.Add(options.CallbackCost, options.Period));
            }

            var createElapsed = Stopwatch.GetElapsedTime(createStart);
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
            await Task.Delay(options.Duration, cancellationToken).ConfigureAwait(false);
            var cpuAfter = Process.GetCurrentProcess().TotalProcessorTime;
            var allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
            var activeTimerCount = fixture.Scheduler.Descriptors.Count;

            var destroyStart = Stopwatch.GetTimestamp();
            foreach (var timerId in timerIds)
            {
                fixture.Destroy(timerId);
            }

            var destroyElapsed = Stopwatch.GetElapsedTime(destroyStart);
            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);

            var enteredTicks = TimerBenchmarkCallback.EnteredTicks;
            return new TimerBenchmarkScenarioResult(
                options,
                EnteredTicks: enteredTicks,
                P50DispatchLatency: observer.GetLatencyPercentile(0.50),
                P95DispatchLatency: observer.GetLatencyPercentile(0.95),
                P99DispatchLatency: observer.GetLatencyPercentile(0.99),
                ThroughputPerSecond: enteredTicks / Math.Max(options.Duration.TotalSeconds, 0.001),
                SkippedTicks: observer.SkippedTicks,
                CallbackFailures: observer.CallbackFailures,
                MaxQueueDepth: observer.MaxQueueDepth,
                QueueFullSkips: observer.QueueFullSkips,
                MaxActiveWorkers: observer.MaxActiveWorkers,
                AllocatedBytesPerTick: enteredTicks == 0 ? 0 : (allocatedAfter - allocatedBefore) / enteredTicks,
                CpuTime: cpuAfter - cpuBefore,
                CreateLatency: createElapsed,
                DestroyLatency: destroyElapsed,
                ActiveTimerCount: activeTimerCount,
                HeapStaleEntryCount: observer.HeapStaleEntryCount);
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
            foreach (var scenario in run.Scenarios)
            {
                builder.AppendLine(
                    $"scenario timerCount={scenario.Options.TimerCount} periodMs={scenario.Options.Period.TotalMilliseconds} callbackCost={FormatCallbackCost(scenario.Options.CallbackCost)} " +
                    $"p50DispatchLatencyMs={scenario.P50DispatchLatency.TotalMilliseconds:F3} p95DispatchLatencyMs={scenario.P95DispatchLatency.TotalMilliseconds:F3} p99DispatchLatencyMs={scenario.P99DispatchLatency.TotalMilliseconds:F3} " +
                    $"throughput={scenario.ThroughputPerSecond:F2}/s skippedTicks={scenario.SkippedTicks} callbackFailures={scenario.CallbackFailures} queueDepth={scenario.MaxQueueDepth} queueFullSkips={scenario.QueueFullSkips} " +
                    $"activeWorkerCount={scenario.MaxActiveWorkers} allocatedBytesPerTick={scenario.AllocatedBytesPerTick} cpuTimeMs={scenario.CpuTime.TotalMilliseconds:F3} " +
                    $"createLatencyMs={scenario.CreateLatency.TotalMilliseconds:F3} destroyLatencyMs={scenario.DestroyLatency.TotalMilliseconds:F3} activeTimerCount={scenario.ActiveTimerCount} heapStaleEntryCount={scenario.HeapStaleEntryCount}");
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
                    sourceKind: null,
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
        private readonly ConcurrentBag<double> latencyMilliseconds = [];
        private int queueDepth;
        private int activeWorkers;
        private int maxQueueDepth;
        private int maxActiveWorkers;
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

        public void OnDispatchQueued(LakonaTimerDispatchObservation observation)
        {
            var current = Interlocked.Increment(ref queueDepth);
            UpdateMax(ref maxQueueDepth, current);
        }

        public void OnDispatchQueueFull(LakonaTimerDispatchObservation observation)
        {
            Interlocked.Increment(ref queueFullSkips);
        }

        public void OnDispatchSkipped(LakonaTimerDispatchObservation observation)
        {
            Interlocked.Increment(ref skippedTicks);
        }

        public void OnDispatchStarted(LakonaTimerDispatchObservation observation)
        {
            Interlocked.Decrement(ref queueDepth);
            var current = Interlocked.Increment(ref activeWorkers);
            UpdateMax(ref maxActiveWorkers, current);
            latencyMilliseconds.Add(Math.Max(0, (DateTimeOffset.UtcNow - observation.DueAtUtc).TotalMilliseconds));
        }

        public void OnDispatchFailed(LakonaTimerDispatchObservation observation, Exception exception)
        {
            Interlocked.Increment(ref callbackFailures);
            Interlocked.Decrement(ref activeWorkers);
        }

        public void OnDispatchCompleted(LakonaTimerDispatchObservation observation)
        {
            Interlocked.Decrement(ref activeWorkers);
        }

        public void OnStaleHeapEntry(LakonaTimerHeapObservation observation)
        {
            Interlocked.Increment(ref heapStaleEntryCount);
        }

        public TimeSpan GetLatencyPercentile(double percentile)
        {
            var values = latencyMilliseconds.ToArray();
            if (values.Length == 0)
            {
                return TimeSpan.Zero;
            }

            Array.Sort(values);
            var index = Math.Clamp((int)Math.Ceiling(values.Length * percentile) - 1, 0, values.Length - 1);
            return TimeSpan.FromMilliseconds(values[index]);
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
        long EnteredTicks,
        TimeSpan P50DispatchLatency,
        TimeSpan P95DispatchLatency,
        TimeSpan P99DispatchLatency,
        double ThroughputPerSecond,
        long SkippedTicks,
        long CallbackFailures,
        int MaxQueueDepth,
        long QueueFullSkips,
        int MaxActiveWorkers,
        long AllocatedBytesPerTick,
        TimeSpan CpuTime,
        TimeSpan CreateLatency,
        TimeSpan DestroyLatency,
        int ActiveTimerCount,
        long HeapStaleEntryCount);

    public sealed record TimerBenchmarkArgs(TimerCallbackCost Cost);

    public sealed class TimerBenchmarkCallback
    {
        private static long enteredTicks;
        private static long actorState;

        public static long EnteredTicks => Volatile.Read(ref enteredTicks);

        public static ValueTask TickAsync(TimerTick<TimerBenchmarkArgs> tick)
        {
            Interlocked.Increment(ref enteredTicks);
            switch (tick.Args.Cost)
            {
                case TimerCallbackCost.Empty:
                    break;
                case TimerCallbackCost.Actor:
                    Interlocked.Increment(ref actorState);
                    break;
                case TimerCallbackCost.SimulatedRoomBroadcast:
                    Span<byte> payload = stackalloc byte[256];
                    payload.Fill((byte)(enteredTicks & 0xFF));
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported callback cost '{tick.Args.Cost}'.");
            }

            return default;
        }

        public static void Reset()
        {
            Volatile.Write(ref enteredTicks, 0);
            Volatile.Write(ref actorState, 0);
        }
    }
}
