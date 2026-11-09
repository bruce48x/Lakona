using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime;
using System.Text.Json;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Observability;
using Lakona.Game.Server.Sessions;

var results = new List<object>();
var modeSelection = args.Length > 1 ? args[1] : "all";
foreach (var population in new[] { 10000, 50000 })
{
    if (modeSelection != "operations")
    {
        for (var round = 0; round < 5; round++)
        foreach (var candidate in round % 2 == 0 ? new[] { false, true } : new[] { true, false })
        {
            var clock = new ManualTime();
            using var scenario = new Scenario(population, candidate, clock);
            foreach (var mode in new[] { "direct", "refresh", "hit" })
            {
                void Read()
                {
                    if (mode == "direct")
                    {
                        if (scenario.Registry.GetDiagnosticsSnapshot().ActiveSessions != population)
                            throw new InvalidOperationException("Wrong direct counts.");
                    }
                    else
                    {
                        if (mode == "refresh") clock.Advance();
                        scenario.Collect();
                        if (scenario.Total != population) throw new InvalidOperationException("Wrong metric total.");
                    }
                }
                for (var i = 0; i < 20; i++) Read();
                var count = candidate && mode == "hit" ? 10000 : 100;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var gen0 = GC.CollectionCount(0);
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                for (var i = 0; i < count; i++) Read();
                var elapsed = Stopwatch.GetElapsedTime(start);
                var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                results.Add(new { Population = population, Candidate = candidate, Round = round, Mode = mode,
                    Iterations = count, NsPerRead = elapsed.TotalNanoseconds / count,
                    BytesPerRead = (double)bytes / count, Gen0 = GC.CollectionCount(0) - gen0 });
            }
        }
        Console.WriteLine($"Measured isolated collection: {population}");
    }
    if (modeSelection == "collection") continue;
    // Both readers operate on one actual registry. Only the diagnostic method
    // selected by the probe changes; business methods and identities are shared.
    using var baselineOperations = new Scenario(population, false, TimeProvider.System, commonRegistry: true);
    using var candidateOperations = new Scenario(population, true, TimeProvider.System, shared: baselineOperations);
    for (var round = 0; round < 3; round++)
    foreach (var collect in round % 2 == 0 ? new[] { false, true } : new[] { true, false })
    foreach (var candidate in round % 2 == 0 ? new[] { false, true } : new[] { true, false })
    {
        var scenario = candidate ? candidateOperations : baselineOperations;
        scenario.SelectDiagnostics();
        for (var i = 0; i < 10000; i++) scenario.Operate(i);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var ready = new ManualResetEventSlim();
        using var startGate = new ManualResetEventSlim();
        var stopped = 0;
        long operations = 0, workerBytes = 0;
        double duration = 0;
        var latencies = new double[262144];
        var samples = 0;
        Exception? workerError = null;
        var worker = new Thread(() =>
        {
            ready.Set();
            startGate.Wait();
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            try
            {
                while (Volatile.Read(ref stopped) == 0)
                {
                    var sample = operations % 127 == 0 && samples < latencies.Length;
                    var stamp = sample ? Stopwatch.GetTimestamp() : 0;
                    scenario.Operate(operations);
                    if (sample) latencies[samples++] = Stopwatch.GetElapsedTime(stamp).TotalNanoseconds;
                    operations++;
                }
            }
            catch (Exception error) { workerError = error; }
            duration = Stopwatch.GetElapsedTime(start).TotalSeconds;
            workerBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        });
        worker.Start();
        ready.Wait();
        var collectionBytes = GC.GetAllocatedBytesForCurrentThread();
        var collectionTicks = 0L;
        var sampleStart = Stopwatch.GetTimestamp();
        startGate.Set();
        try
        {
            for (var tick = 0; tick < 20; tick++)
            {
                var remaining = TimeSpan.FromMilliseconds(tick * 100) - Stopwatch.GetElapsedTime(sampleStart);
                if (remaining > TimeSpan.Zero) Thread.Sleep(remaining);
                if (collect)
                {
                    var stamp = Stopwatch.GetTimestamp();
                    scenario.Collect();
                    collectionTicks += Stopwatch.GetTimestamp() - stamp;
                }
            }
            var tail = TimeSpan.FromSeconds(2) - Stopwatch.GetElapsedTime(sampleStart);
            if (tail > TimeSpan.Zero) Thread.Sleep(tail);
        }
        finally
        {
            Volatile.Write(ref stopped, 1);
            worker.Join();
        }
        collectionBytes = GC.GetAllocatedBytesForCurrentThread() - collectionBytes;
        if (workerError is not null) throw workerError;
        if (operations == 0 || samples == 0) throw new InvalidOperationException("No operations observed.");
        var snapshot = scenario.Registry.GetDiagnosticsSnapshot();
        if (snapshot.TotalSessions != population || snapshot.ActiveSessions != population)
            throw new InvalidOperationException("Session state changed after workload.");
        Array.Sort(latencies, 0, samples);
        results.Add(new { Population = population, Candidate = candidate, Round = round,
            Mode = collect ? "operations-with-metrics" : "operations-only", Operations = operations,
            OperationsPerSecond = operations / duration, WorkerBytesPerOperation = (double)workerBytes / operations,
            SampleCount = samples, P50Ns = latencies[(samples - 1) / 2], P95Ns = latencies[(int)((samples - 1) * .95)],
            P99Ns = latencies[(int)((samples - 1) * .99)], CollectionBytes = collectionBytes,
            CollectionMs = collectionTicks * 1000.0 / Stopwatch.Frequency });
        Console.WriteLine($"Measured operations: {population}, candidate={candidate}, collect={collect}, round={round}");
    }
}
File.WriteAllText(args[0], JsonSerializer.Serialize(new { Environment.Version, OS = Environment.OSVersion.ToString(),
    Environment.ProcessorCount, GCSettings.IsServerGC, Results = results }, new JsonSerializerOptions { WriteIndented = true }));

sealed class ManualTime : TimeProvider
{
    private long timestamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => timestamp;
    public void Advance() => timestamp += TimeSpan.TicksPerSecond;
}

sealed class Scenario : IDisposable
{
    private readonly IDisposable telemetry;
    private readonly MeterListener listener = new();
    private readonly GameSessionKey[] sessions;
    private readonly string[] connections;
    private readonly bool candidate;
    public IGameSessionRegistry Registry { get; }
    public long Total { get; private set; }

    public Scenario(int population, bool candidate, TimeProvider time, bool commonRegistry = false, Scenario? shared = null)
    {
        this.candidate = candidate;
        Registry = shared?.Registry ?? (commonRegistry ? new ProbeRegistry(new LakonaGameHostingOptions(), time)
            : candidate ? new InMemoryGameSessionRegistry(new LakonaGameHostingOptions(), time)
            : new BaselineRegistry(new LakonaGameHostingOptions(), time));
        sessions = shared?.sessions ?? new GameSessionKey[population];
        connections = shared?.connections ?? new string[population];
        for (var i = 0; shared is null && i < population; i++)
        {
            sessions[i] = Registry.StartNewSessionAsync("owner").GetAwaiter().GetResult();
            connections[i] = $"connection-{i}";
            Registry.BindSessionAsync(sessions[i], connections[i]).GetAwaiter().GetResult();
        }
        var publishing = false;
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (publishing && instrument.Meter.Name == LakonaGameServerTelemetry.SessionMeterName)
                current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "lakona.game.session.total") Total = value;
        });
        listener.Start();
        publishing = true;
        telemetry = candidate ? new GameSessionPopulationTelemetry(Registry, time) : new BaselineTelemetry(Registry);
        publishing = false;
    }

    public void Collect() => listener.RecordObservableInstruments();

    public void SelectDiagnostics()
    {
        if (Registry is ProbeRegistry probe) probe.UseBaselineDiagnostics = !candidate;
    }

    public void Operate(long sequence)
    {
        var index = (int)(sequence % sessions.Length);
        // Rotate through the entire population. One in sixteen operations is a
        // disconnect/rebind pair; the others validate an active heartbeat.
        if (sequence % 16 == 0)
        {
            Registry.MarkConnectionDisconnectedAsync(connections[index]).GetAwaiter().GetResult();
            Registry.BindSessionAsync(sessions[index], connections[index]).GetAwaiter().GetResult();
        }
        else if (Registry.RecordHeartbeatAsync(connections[index], DateTimeOffset.UnixEpoch).GetAwaiter().GetResult().Status != GameSessionHeartbeatStatus.ActiveSession)
            throw new InvalidOperationException("Heartbeat lost active session.");
    }

    public void Dispose()
    {
        telemetry.Dispose();
        listener.Dispose();
    }
}
