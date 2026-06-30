using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using GameActor = Lakona.Game.Server.Actors.Actor;

namespace Lakona.Game.Server.Tests;

[Collection(HotfixDispatchCollectionNames.GlobalState)]
public sealed class HotfixActorTickSchedulerPerformanceTests : IDisposable
{
    private const string ActorRuntimeKind = "real LakonaActorRuntime";
    private readonly ITestOutputHelper _output;

    public HotfixActorTickSchedulerPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Active_actor_ticks_report_smoke_or_full_benchmark()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = TimerBenchmarkOptions.FromEnvironment();
        WriteRuntimeMetadata(options);

        foreach (var actorCount in options.ActorCounts)
        {
            for (var iteration = 1; iteration <= options.Iterations; iteration++)
            {
                var result = await RunActiveActorScenarioAsync(
                    actorCount,
                    TimeSpan.FromMilliseconds(50),
                    TickBacklogPolicy.SkipIfPending,
                    options,
                    iteration,
                    cancellationToken);

                WriteResult(result);
                Assert.True(result.EnteredTicks > 0);
                Assert.Equal(0, result.RejectedTicks);
            }
        }
    }

    [Fact]
    public async Task Fixed_singleton_ticks_report_smoke_or_full_benchmark()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = TimerBenchmarkOptions.FromEnvironment();
        WriteRuntimeMetadata(options);

        for (var iteration = 1; iteration <= options.Iterations; iteration++)
        {
            var result = await RunFixedActorScenarioAsync(
                TimeSpan.FromMilliseconds(250),
                TickBacklogPolicy.Coalesce,
                options,
                iteration,
                cancellationToken);

            WriteResult(result);
            Assert.True(result.EnteredTicks > 0);
            Assert.Equal(0, result.RejectedTicks);
        }
    }

    [Fact]
    public async Task Missing_fixed_actor_ticks_report_rejections_without_creating_actor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = TimerBenchmarkOptions.FromEnvironment();
        WriteRuntimeMetadata(options);

        var result = await RunMissingActorScenarioAsync(
            TimeSpan.FromMilliseconds(250),
            TickBacklogPolicy.SkipIfPending,
            options,
            cancellationToken);

        WriteResult(result);
        Assert.Equal(0, result.EnteredTicks);
        Assert.True(result.RejectedTicks > 0);
    }

    [Fact]
    public async Task Busy_fixed_actor_ticks_report_backlog_policy_behavior()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = TimerBenchmarkOptions.FromEnvironment();
        WriteRuntimeMetadata(options);

        foreach (var policy in new[] { TickBacklogPolicy.SkipIfPending, TickBacklogPolicy.Coalesce })
        {
            var result = await RunBusyFixedActorScenarioAsync(
                policy,
                options,
                cancellationToken);

            WriteResult(result);
            Assert.True(result.EnteredTicks > 0);
            if (policy == TickBacklogPolicy.Coalesce)
            {
                Assert.True(result.CoalescedTicks > 0);
            }
            else
            {
                Assert.True(result.SkippedTicks > 0);
            }
        }
    }

    public void Dispose()
    {
        HotfixDispatch.Replace(new HotfixDispatchTable(0, Array.Empty<HotfixMethodBinding>()));
    }

    private static ServiceProvider CreateProvider()
    {
        return new ServiceCollection()
            .AddLakonaGameServerActors(options => options.MailboxCapacity = 8192)
            .BuildServiceProvider();
    }

    private async Task<TimerBenchmarkResult> RunActiveActorScenarioAsync(
        int actorCount,
        TimeSpan interval,
        TickBacklogPolicy backlogPolicy,
        TimerBenchmarkOptions options,
        int iteration,
        CancellationToken cancellationToken)
    {
        await using var provider = CreateProvider();
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var observer = new BenchmarkTickObserver();
        await using var scheduler = new HotfixActorTickScheduler(
            provider.GetRequiredService<IActorRuntime>(),
            NullLogger<HotfixActorTickScheduler>.Instance,
            observer);

        HotfixDispatch.Replace(CreateTickTable(typeof(BenchmarkRoomActor), nameof(BenchmarkRoomBehavior.TickAsync)));
        for (var i = 0; i < actorCount; i++)
        {
            var created = await lifecycle.CreateLocalAsync<BenchmarkRoomActor>(
                ActorId.From($"bench-room/{i:D5}"),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            Assert.True(created.Succeeded, created.Diagnostic);
        }

        scheduler.Apply(CreateSnapshot(new HotfixActorTickDeclaration(
            HotfixActorTickMode.ActiveActors,
            typeof(BenchmarkRoomActor),
            "",
            nameof(BenchmarkRoomBehavior.TickAsync),
            interval,
            backlogPolicy)));

        return await MeasureAsync(
            observer,
            "active-room-skipifpending",
            actorCount,
            interval,
            backlogPolicy,
            options,
            iteration,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<TimerBenchmarkResult> RunFixedActorScenarioAsync(
        TimeSpan interval,
        TickBacklogPolicy backlogPolicy,
        TimerBenchmarkOptions options,
        int iteration,
        CancellationToken cancellationToken)
    {
        await using var provider = CreateProvider();
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var observer = new BenchmarkTickObserver();
        var actorId = ActorId.From($"fixed/{iteration}");
        await using var scheduler = new HotfixActorTickScheduler(
            provider.GetRequiredService<IActorRuntime>(),
            NullLogger<HotfixActorTickScheduler>.Instance,
            observer);

        HotfixDispatch.Replace(CreateTickTable(typeof(BenchmarkRoomActor), nameof(BenchmarkRoomBehavior.TickAsync)));
        var created = await lifecycle.CreateLocalAsync<BenchmarkRoomActor>(
            actorId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        Assert.True(created.Succeeded, created.Diagnostic);

        scheduler.Apply(CreateSnapshot(new HotfixActorTickDeclaration(
            HotfixActorTickMode.FixedActor,
            typeof(BenchmarkRoomActor),
            actorId.Value,
            nameof(BenchmarkRoomBehavior.TickAsync),
            interval,
            backlogPolicy)));

        return await MeasureAsync(
            observer,
            "fixed-singleton-coalesce",
            1,
            interval,
            backlogPolicy,
            options,
            iteration,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<TimerBenchmarkResult> RunMissingActorScenarioAsync(
        TimeSpan interval,
        TickBacklogPolicy backlogPolicy,
        TimerBenchmarkOptions options,
        CancellationToken cancellationToken)
    {
        await using var provider = CreateProvider();
        var observer = new BenchmarkTickObserver();
        await using var scheduler = new HotfixActorTickScheduler(
            provider.GetRequiredService<IActorRuntime>(),
            NullLogger<HotfixActorTickScheduler>.Instance,
            observer);

        HotfixDispatch.Replace(CreateTickTable(typeof(BenchmarkRoomActor), nameof(BenchmarkRoomBehavior.TickAsync)));
        scheduler.Apply(CreateSnapshot(new HotfixActorTickDeclaration(
            HotfixActorTickMode.FixedActor,
            typeof(BenchmarkRoomActor),
            "missing-room",
            nameof(BenchmarkRoomBehavior.TickAsync),
            interval,
            backlogPolicy)));

        return await MeasureAsync(
            observer,
            "missing-fixed-actor",
            1,
            interval,
            backlogPolicy,
            options,
            1,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<TimerBenchmarkResult> RunBusyFixedActorScenarioAsync(
        TickBacklogPolicy backlogPolicy,
        TimerBenchmarkOptions options,
        CancellationToken cancellationToken)
    {
        await using var provider = CreateProvider();
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var observer = new BenchmarkTickObserver();
        var actorId = ActorId.From($"busy/{backlogPolicy}");
        await using var scheduler = new HotfixActorTickScheduler(
            provider.GetRequiredService<IActorRuntime>(),
            NullLogger<HotfixActorTickScheduler>.Instance,
            observer);

        HotfixDispatch.Replace(CreateTickTable(typeof(BusyBenchmarkActor), nameof(BusyBenchmarkBehavior.TickAsync)));
        var created = await lifecycle.CreateLocalAsync<BusyBenchmarkActor>(
            actorId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        Assert.True(created.Succeeded, created.Diagnostic);

        scheduler.Apply(CreateSnapshot(new HotfixActorTickDeclaration(
            HotfixActorTickMode.FixedActor,
            typeof(BusyBenchmarkActor),
            actorId.Value,
            nameof(BusyBenchmarkBehavior.TickAsync),
            TimeSpan.FromMilliseconds(10),
            backlogPolicy)));

        return await MeasureAsync(
            observer,
            $"busy-fixed-{backlogPolicy.ToString().ToLowerInvariant()}",
            1,
            TimeSpan.FromMilliseconds(10),
            backlogPolicy,
            options,
            1,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<TimerBenchmarkResult> MeasureAsync(
        BenchmarkTickObserver observer,
        string scenario,
        int actorCount,
        TimeSpan interval,
        TickBacklogPolicy backlogPolicy,
        TimerBenchmarkOptions options,
        int iteration,
        CancellationToken cancellationToken)
    {
        await Task.Delay(options.Warmup, cancellationToken).ConfigureAwait(false);
        observer.StartMeasurement();
        var process = Process.GetCurrentProcess();
        var cpuStart = process.TotalProcessorTime;
        var allocatedStart = GC.GetTotalAllocatedBytes(precise: true);
        var elapsed = Stopwatch.StartNew();
        await Task.Delay(options.Duration, cancellationToken).ConfigureAwait(false);
        elapsed.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedStart;
        var cpu = process.TotalProcessorTime - cpuStart;

        return observer.CreateResult(
            scenario,
            actorCount,
            interval,
            backlogPolicy,
            options,
            iteration,
            elapsed.Elapsed,
            allocated,
            cpu);
    }

    private static HotfixSnapshot CreateSnapshot(params HotfixActorTickDeclaration[] ticks)
    {
        var feature = new HotfixFeatureDeclaration(
            "timer-benchmark",
            typeof(HotfixActorTickSchedulerPerformanceTests),
            Discoverable: true,
            new Dictionary<string, string>(),
            [],
            ticks,
            [],
            []);

        return new HotfixSnapshot(
            "benchmark",
            "benchmark.dll",
            null,
            DateTimeOffset.UtcNow,
            1,
            [],
            HotfixReloadStatus.Succeeded,
            null,
            null,
            [feature]);
    }

    private static HotfixDispatchTable CreateTickTable(Type actorType, string methodName)
    {
        var behaviorType = actorType == typeof(BusyBenchmarkActor)
            ? typeof(BusyBenchmarkBehavior)
            : typeof(BenchmarkRoomBehavior);
        var method = behaviorType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!;
        var binding = new HotfixMethodBinding(
            HotfixDispatch.CreateKey(
                actorType,
                methodName,
                typeof(ValueTask),
                [typeof(HotfixActorTick)]),
            method,
            actorType,
            typeof(ValueTask),
            [typeof(HotfixActorTick)]);
        return new HotfixDispatchTable(1, [binding]);
    }

#if DEBUG
    private const string BuildConfiguration = "Debug";
#else
    private const string BuildConfiguration = "Release";
#endif

    private void WriteRuntimeMetadata(TimerBenchmarkOptions options)
    {
        _output.WriteLine($"OS: {RuntimeInformation.OSDescription}");
        _output.WriteLine($"CPU: {GetCpuModel()}");
        _output.WriteLine($"SDK: {GetDotNetSdkVersion()}");
        _output.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        _output.WriteLine($"BuildConfiguration: {BuildConfiguration}");
        _output.WriteLine($"ProcessArchitecture: {RuntimeInformation.ProcessArchitecture}");
        _output.WriteLine($"ProcessBitness: {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}");
        _output.WriteLine($"LogicalProcessors: {Environment.ProcessorCount}");
        _output.WriteLine($"ActorRuntime: {ActorRuntimeKind}");
        _output.WriteLine($"Mode: {(options.Full ? "full" : "smoke")}");
        _output.WriteLine($"Warmup: {options.Warmup}");
        _output.WriteLine($"Duration: {options.Duration}");
        _output.WriteLine($"Iterations: {options.Iterations}");
        _output.WriteLine($"ActorCounts: {string.Join(", ", options.ActorCounts)}");
    }

    private void WriteResult(TimerBenchmarkResult result)
    {
        _output.WriteLine($"Scenario: {result.Scenario}");
        _output.WriteLine($"ActorCount: {result.ActorCount}");
        _output.WriteLine($"Interval: {result.Interval.TotalMilliseconds} ms");
        _output.WriteLine($"BacklogPolicy: {result.BacklogPolicy}");
        _output.WriteLine($"Duration: {result.Duration}");
        _output.WriteLine($"Iteration: {result.Iteration}");
        _output.WriteLine($"ExpectedOpportunities: {result.ExpectedTickOpportunities}");
        _output.WriteLine($"Accepted: {result.AcceptedDispatches}");
        _output.WriteLine($"Entered: {result.EnteredTicks}");
        _output.WriteLine($"Skipped: {result.SkippedTicks}");
        _output.WriteLine($"Coalesced: {result.CoalescedTicks}");
        _output.WriteLine($"Rejected: {result.RejectedTicks}");
        _output.WriteLine($"P50Latency: {result.P50.TotalMilliseconds:F3} ms");
        _output.WriteLine($"P95Latency: {result.P95.TotalMilliseconds:F3} ms");
        _output.WriteLine($"P99Latency: {result.P99.TotalMilliseconds:F3} ms");
        _output.WriteLine($"AllocatedBytes: {result.AllocatedBytes}");
        _output.WriteLine($"CpuTime: {result.CpuTime}");
    }

    private static string GetCpuModel()
    {
        try
        {
            return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")
                ?? Environment.GetEnvironmentVariable("PROCESSOR_MODEL")
                ?? "unavailable";
        }
        catch
        {
            return "unavailable";
        }
    }

    private static string GetDotNetSdkVersion()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null || !process.WaitForExit(2_000))
            {
                return "unavailable";
            }

            return process.ExitCode == 0
                ? process.StandardOutput.ReadToEnd().Trim()
                : "unavailable";
        }
        catch
        {
            return "unavailable";
        }
    }

    private sealed class BenchmarkRoomActor : GameActor
    {
        public long TickCount;
    }

    private sealed class BusyBenchmarkActor : GameActor
    {
        public long TickCount;
    }

    private static class BenchmarkRoomBehavior
    {
        public static ValueTask TickAsync(BenchmarkRoomActor actor, HotfixActorTick tick)
        {
            _ = tick;
            Interlocked.Increment(ref actor.TickCount);
            return default;
        }
    }

    private static class BusyBenchmarkBehavior
    {
        public static ValueTask TickAsync(BusyBenchmarkActor actor, HotfixActorTick tick)
        {
            _ = tick;
            Interlocked.Increment(ref actor.TickCount);
            var stopAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 50;
            while (Stopwatch.GetTimestamp() < stopAt)
            {
            }

            return default;
        }
    }

    private sealed class BenchmarkTickObserver : IHotfixActorTickSchedulerObserver
    {
        private readonly ConcurrentQueue<TimeSpan> _latencies = new();
        private long _accepted;
        private long _rejected;
        private long _skipped;
        private long _coalesced;
        private long _entered;
        private long _measurementStartTimestamp = long.MaxValue;

        public void StartMeasurement()
        {
            while (_latencies.TryDequeue(out _))
            {
            }

            Interlocked.Exchange(ref _accepted, 0);
            Interlocked.Exchange(ref _rejected, 0);
            Interlocked.Exchange(ref _skipped, 0);
            Interlocked.Exchange(ref _coalesced, 0);
            Interlocked.Exchange(ref _entered, 0);
            Interlocked.Exchange(ref _measurementStartTimestamp, Stopwatch.GetTimestamp());
        }

        public void OnDispatchAccepted(HotfixActorTickDispatchObservation observation)
        {
            if (IsMeasured(observation.QueuedTimestamp))
            {
                Interlocked.Increment(ref _accepted);
            }
        }

        public void OnDispatchRejected(HotfixActorTickDispatchObservation observation, ActorTellResult result)
        {
            _ = result;
            if (IsMeasured(observation.QueuedTimestamp))
            {
                Interlocked.Increment(ref _rejected);
            }
        }

        public void OnDispatchSkipped(HotfixActorTickDispatchObservation observation)
        {
            if (IsMeasured(observation.QueuedTimestamp))
            {
                Interlocked.Increment(ref _skipped);
            }
        }

        public void OnDispatchCoalesced(HotfixActorTickDispatchObservation observation)
        {
            if (IsMeasured(observation.QueuedTimestamp))
            {
                Interlocked.Increment(ref _coalesced);
            }
        }

        public void OnTickEntered(HotfixActorTickEntryObservation observation)
        {
            if (!IsMeasured(observation.QueuedTimestamp))
            {
                return;
            }

            Interlocked.Increment(ref _entered);
            _latencies.Enqueue(observation.QueueLatency);
        }

        public TimerBenchmarkResult CreateResult(
            string scenario,
            int actorCount,
            TimeSpan interval,
            TickBacklogPolicy backlogPolicy,
            TimerBenchmarkOptions options,
            int iteration,
            TimeSpan duration,
            long allocatedBytes,
            TimeSpan cpuTime)
        {
            var latencies = _latencies.ToArray();
            Array.Sort(latencies);

            return new TimerBenchmarkResult(
                scenario,
                actorCount,
                interval,
                backlogPolicy,
                iteration,
                duration,
                ExpectedTickOpportunities: (long)Math.Floor(duration.TotalMilliseconds / interval.TotalMilliseconds) * actorCount,
                AcceptedDispatches: Interlocked.Read(ref _accepted),
                EnteredTicks: Interlocked.Read(ref _entered),
                SkippedTicks: Interlocked.Read(ref _skipped),
                CoalescedTicks: Interlocked.Read(ref _coalesced),
                RejectedTicks: Interlocked.Read(ref _rejected),
                P50: Percentile(latencies, 0.50),
                P95: Percentile(latencies, 0.95),
                P99: Percentile(latencies, 0.99),
                allocatedBytes,
                cpuTime,
                options.Full);
        }

        private static TimeSpan Percentile(TimeSpan[] values, double percentile)
        {
            if (values.Length == 0)
            {
                return TimeSpan.Zero;
            }

            var index = Math.Clamp((int)Math.Ceiling(values.Length * percentile) - 1, 0, values.Length - 1);
            return values[index];
        }

        private bool IsMeasured(long queuedTimestamp)
        {
            return queuedTimestamp >= Interlocked.Read(ref _measurementStartTimestamp);
        }
    }

    private sealed record TimerBenchmarkOptions(
        bool Full,
        int[] ActorCounts,
        TimeSpan Warmup,
        TimeSpan Duration,
        int Iterations)
    {
        public static TimerBenchmarkOptions FromEnvironment()
        {
            var full = string.Equals(
                Environment.GetEnvironmentVariable("LAKONA_TIMER_BENCHMARK_FULL"),
                "1",
                StringComparison.Ordinal);

            return full
                ? new TimerBenchmarkOptions(
                    true,
                    [100, 1_000, 10_000],
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(10),
                    3)
                : new TimerBenchmarkOptions(
                    false,
                    [100],
                    TimeSpan.FromMilliseconds(100),
                    TimeSpan.FromMilliseconds(500),
                    1);
        }
    }

    private sealed record TimerBenchmarkResult(
        string Scenario,
        int ActorCount,
        TimeSpan Interval,
        TickBacklogPolicy BacklogPolicy,
        int Iteration,
        TimeSpan Duration,
        long ExpectedTickOpportunities,
        long AcceptedDispatches,
        long EnteredTicks,
        long SkippedTicks,
        long CoalescedTicks,
        long RejectedTicks,
        TimeSpan P50,
        TimeSpan P95,
        TimeSpan P99,
        long AllocatedBytes,
        TimeSpan CpuTime,
        bool Full);
}
