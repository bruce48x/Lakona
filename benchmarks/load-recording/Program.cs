using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using CurrentRecorder = Lakona.Game.LoadTesting.Internal.LoadRunRecorder;
using BaselineRecorder = LoadRecording.Baseline.LoadRunRecorder;

const int operations = 20_000_000;
const int warmupPerWorker = 100_000;
const int repetitions = 5;
var samples = new List<object>();
using var process = Process.GetCurrentProcess();
foreach (var workers in new[] { 1, 8 })
{
    foreach (var mixed in new[] { false, true })
    {
        for (var round = 0; round < repetitions; round++)
        {
            foreach (var baseline in round % 2 == 0 ? new[] { true, false } : new[] { false, true })
            {
                Action<string, TimeSpan> record = baseline
                    ? new BaselineRecorder("benchmark", workers).RecordSucceededOperation
                    : new CurrentRecorder("benchmark", workers).RecordSucceededOperation;
                record("send", TimeSpan.Zero);
                var beforeNewOperation = GC.GetAllocatedBytesForCurrentThread();
                record("new-operation", TimeSpan.Zero);
                var bytesPerOperationName = GC.GetAllocatedBytesForCurrentThread() - beforeNewOperation;
                using var ready = new CountdownEvent(workers);
                using var start = new ManualResetEventSlim();
                var tasks = Enumerable.Range(0, workers).Select(_ => Task.Factory.StartNew(() =>
                {
                    for (var index = 0; index < warmupPerWorker; index++) Record(index);
                    ready.Signal();
                    start.Wait();
                    for (var index = 0; index < operations / workers; index++) Record(index);

                    void Record(int index)
                    {
                        var ticks = mixed ? ((index % 4) switch
                        {
                            0 => 10L,
                            1 => TimeSpan.TicksPerMillisecond,
                            2 => 100 * TimeSpan.TicksPerMillisecond,
                            _ => TimeSpan.TicksPerHour
                        }) : TimeSpan.TicksPerMillisecond;
                        record("send", TimeSpan.FromTicks(ticks));
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
                ready.Wait();
                var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
                var cpuBefore = process.TotalProcessorTime;
                var timestamp = Stopwatch.GetTimestamp();
                start.Set();
                Task.WaitAll(tasks);
                var elapsed = Stopwatch.GetElapsedTime(timestamp);
                var cpu = process.TotalProcessorTime - cpuBefore;
                var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
                var result = new
                {
                    implementation = baseline ? "baseline" : "current",
                    workers, mixed, round, operations, warmupPerWorker,
                    nsPerRecord = elapsed.TotalNanoseconds / operations,
                    recordsPerSecond = operations / elapsed.TotalSeconds,
                    cpuMilliseconds = cpu.TotalMilliseconds,
                    allocatedBytes = allocated,
                    bytesPerOperationName
                };
                samples.Add(result);
                Console.WriteLine(JsonSerializer.Serialize(result));
                GC.KeepAlive(record);
            }
        }
    }
}
var report = new
{
    environment = new
    {
        runtime = RuntimeInformation.FrameworkDescription,
        os = RuntimeInformation.OSDescription,
        architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        logicalProcessors = Environment.ProcessorCount,
        serverGC = GCSettings.IsServerGC,
        tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
        configuration = "Release"
    },
    samples
};
File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
