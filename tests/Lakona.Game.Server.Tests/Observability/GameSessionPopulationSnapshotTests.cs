using System.Diagnostics.Metrics;
using System.Reflection;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Observability;
using Lakona.Game.Server.Sessions;
using Xunit;

namespace Lakona.Game.Server.Tests.Observability;

[Collection(GameSessionPopulationMetricsCollectionNames.Diagnostics)]
public sealed class GameSessionPopulationSnapshotTests
{
    [Fact]
    public void Gauges_share_one_lazy_snapshot_until_exact_expiry()
    {
        var time = new ManualTime();
        var calls = 0;
        var registry = Registry(() => { calls++; return Counts(calls); });
        var values = new Dictionary<string, long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == LakonaGameServerTelemetry.SessionMeterName)
                current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            Assert.Empty(tags.ToArray());
            values[instrument.Name] = value;
        });
        listener.Start();
        using var telemetry = new GameSessionPopulationTelemetry(registry, time);
        Assert.Equal(0, calls);
        listener.RecordObservableInstruments();
        Assert.Equal(1, calls);
        Assert.Equal(6, values.Count);
        Assert.All(values.Values, value => Assert.Equal(1, value));

        time.Advance(TimeSpan.FromSeconds(1) - TimeSpan.FromTicks(1));
        time.Utc = time.Utc.AddYears(-10); // Wall clock adjustment must not expire the cache.
        listener.RecordObservableInstruments();
        Assert.Equal(1, calls);
        time.Advance(TimeSpan.FromTicks(1));
        listener.RecordObservableInstruments();
        Assert.Equal(2, calls);
        Assert.All(values.Values, value => Assert.Equal(2, value));
    }

    [Fact]
    public void Slow_refresh_gets_reuse_window_after_completion()
    {
        var time = new ManualTime();
        var calls = 0;
        using var telemetry = new GameSessionPopulationTelemetry(Registry(() =>
        {
            calls++;
            time.Advance(TimeSpan.FromSeconds(5));
            return Counts(calls);
        }), time);
        var first = telemetry.GetSnapshot();
        Assert.Same(first, telemetry.GetSnapshot());
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.NotSame(first, telemetry.GetSnapshot());
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_readers_share_initial_or_expired_refresh(bool expired)
    {
        var time = new ManualTime();
        var calls = 0;
        var block = false;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var ready = new CountdownEvent(16);
        using var telemetry = new GameSessionPopulationTelemetry(Registry(() =>
        {
            Interlocked.Increment(ref calls);
            if (block)
            {
                entered.Set();
                release.Wait(TestContext.Current.CancellationToken);
            }
            return Counts(calls);
        }), time);
        if (expired)
        {
            telemetry.GetSnapshot();
            time.Advance(TimeSpan.FromSeconds(1));
        }
        block = true;
        var tasks = Enumerable.Range(0, 16).Select(_ => Task.Factory.StartNew(() =>
        {
            ready.Signal();
            return telemetry.GetSnapshot();
        }, TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        try
        {
            ready.Wait(TestContext.Current.CancellationToken);
            entered.Wait(TestContext.Current.CancellationToken);
            Assert.Equal(expired ? 2 : 1, Volatile.Read(ref calls));
        }
        finally { release.Set(); }
        var results = await Task.WhenAll(tasks);
        Assert.All(results, result => Assert.Same(results[0], result));
        Assert.Equal(expired ? 2 : 1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_refresh_is_not_published_and_can_retry_immediately(bool expired)
    {
        var time = new ManualTime();
        var calls = 0;
        var fail = false;
        var expected = new IOException("snapshot unavailable");
        using var telemetry = new GameSessionPopulationTelemetry(Registry(() =>
        {
            calls++;
            if (fail) throw expected;
            return Counts(calls);
        }), time);
        if (expired)
        {
            telemetry.GetSnapshot();
            time.Advance(TimeSpan.FromSeconds(1));
        }
        fail = true;
        Assert.Same(expected, Assert.Throws<IOException>(() => telemetry.GetSnapshot()));
        fail = false;
        var recovered = telemetry.GetSnapshot();
        Assert.Equal(expired ? 3 : 2, recovered.TotalSessions);
        Assert.Same(recovered, telemetry.GetSnapshot());
        Assert.Equal(expired ? 3 : 2, calls);
    }

    [Fact]
    public async Task Session_changes_are_immediate_for_diagnostics_and_visible_after_metric_expiry()
    {
        var time = new ManualTime();
        var registry = new InMemoryGameSessionRegistry(new LakonaGameHostingOptions(), time);
        using var telemetry = new GameSessionPopulationTelemetry(registry, time);
        var previous = telemetry.GetSnapshot();
        void Check(GameSessionDiagnosticsSnapshot expected)
        {
            Assert.Equal(expected, registry.GetDiagnosticsSnapshot());
            Assert.Same(previous, telemetry.GetSnapshot());
            time.Advance(TimeSpan.FromSeconds(1));
            previous = telemetry.GetSnapshot();
            Assert.Equal(expected, previous);
        }

        var token = TestContext.Current.CancellationToken;
        var session = await registry.StartNewSessionAsync("owner", token);
        Check(new(1, 0, 0, 0, 0, 1));
        await registry.PrepareSessionBindingAsync(session, "a", token);
        Check(new(1, 0, 0, 0, 0, 1));
        await registry.CommitSessionBindingAsync(session, "a", token);
        Check(new(1, 1, 1, 0, 0, 1));
        await registry.MarkConnectionDisconnectedAsync("a", token);
        Check(new(1, 0, 0, 1, 0, 1));
        await registry.PrepareSessionBindingAsync(session, "b", token);
        Check(new(1, 0, 0, 1, 0, 1));
        await registry.RollbackSessionBindingAsync(session, "b", token);
        Check(new(1, 0, 0, 1, 0, 1));
        await registry.BindSessionAsync(session, "c", token);
        Check(new(1, 1, 1, 0, 0, 1));
        await registry.MarkSessionTerminatedAsync(session,
            new SessionTerminationNotice(SessionTerminationReason.Policy, "end"), true, token);
        Check(new(1, 0, 0, 0, 1, 1));
        time.Utc = time.Utc.AddMinutes(2);
        await registry.ExpireSessionsAsync(time.Utc, token);
        Check(new(0, 0, 0, 0, 0, 0));
        var removed = await registry.StartNewSessionAsync("removed", token);
        Check(new(1, 0, 0, 0, 0, 1));
        await registry.RemoveSessionAsync(removed, token);
        Check(new(0, 0, 0, 0, 0, 0));
        var dropped = await registry.StartNewSessionAsync("dropped", token);
        await registry.MarkSessionTerminatedAsync(dropped,
            new SessionTerminationNotice(SessionTerminationReason.Policy, "drop"), false, token);
        Check(new(0, 0, 0, 0, 0, 0));
    }

    [Fact]
    public async Task Concurrent_registry_changes_preserve_per_observation_count_invariants()
    {
        var registry = new InMemoryGameSessionRegistry();
        var token = TestContext.Current.CancellationToken;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writers = Enumerable.Range(0, 4).Select(worker => Task.Run(async () =>
        {
            await start.Task;
            for (var i = 0; i < 500; i++)
            {
                var session = await registry.StartNewSessionAsync($"owner-{worker}", token);
                var connection = $"connection-{worker}-{i}";
                await registry.BindSessionAsync(session, connection, token);
                await registry.MarkConnectionDisconnectedAsync(connection, token);
                await registry.RemoveSessionAsync(session, token);
            }
        }, token)).ToArray();
        var observer = Task.Run(async () =>
        {
            await start.Task;
            for (var i = 0; i < 2000; i++)
            {
                var snapshot = registry.GetDiagnosticsSnapshot();
                Assert.InRange(snapshot.ResumableSessions, 0, snapshot.TotalSessions);
                Assert.InRange(snapshot.ActiveSessions + snapshot.DisconnectedSessions + snapshot.TerminatedSessions, 0, snapshot.TotalSessions);
                Assert.Equal(snapshot.ActiveSessions, snapshot.ActiveConnections);
            }
        }, token);
        start.SetResult();
        await Task.WhenAll(writers.Append(observer));
        Assert.Equal(new GameSessionDiagnosticsSnapshot(0, 0, 0, 0, 0, 0), registry.GetDiagnosticsSnapshot());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(10000)]
    public async Task Direct_scan_allocation_does_not_grow_with_population(int population)
    {
        var registry = new InMemoryGameSessionRegistry();
        for (var i = 0; i < population; i++)
            await registry.StartNewSessionAsync("owner", TestContext.Current.CancellationToken);
        for (var i = 0; i < 20; i++) registry.GetDiagnosticsSnapshot();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var total = 0;
        for (var i = 0; i < 100; i++) total += registry.GetDiagnosticsSnapshot().TotalSessions;
        var allocated = (GC.GetAllocatedBytesForCurrentThread() - before) / 100;
        Assert.Equal(population * 100, total);
        Assert.InRange(allocated, 0, 1024);
    }

    private static GameSessionDiagnosticsSnapshot Counts(int value) => new(value, value, value, value, value, value);

    private static IGameSessionRegistry Registry(Func<GameSessionDiagnosticsSnapshot> read)
    {
        var registry = DispatchProxy.Create<IGameSessionRegistry, SnapshotRegistry>();
        ((SnapshotRegistry)registry).Read = read;
        return registry;
    }

    public class SnapshotRegistry : DispatchProxy
    {
        public Func<GameSessionDiagnosticsSnapshot> Read { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IGameSessionRegistry.GetDiagnosticsSnapshot)
                ? Read() : throw new NotSupportedException();
    }

    private sealed class ManualTime : TimeProvider
    {
        private long timestamp;
        public DateTimeOffset Utc { get; set; } = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Volatile.Read(ref timestamp);
        public override DateTimeOffset GetUtcNow() => Utc;
        public void Advance(TimeSpan duration) => Interlocked.Add(ref timestamp, duration.Ticks);
    }
}
