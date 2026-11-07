using System.Diagnostics;
using System.Net;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.Kcp;
using Lakona.Rpc.Transport.Tcp;

if (args[0] == "idle") { await IdleProbe.RunAsync(args); return; }
ExceptionProbe.Start();
var role = args[0];
using var wireTrace = new WireTrace(role);
var transportName = args[1];
var size = int.Parse(args[2]);
#if KCP_OPTIONS
var kcpOptions = new KcpTransportOptions();
if (Environment.GetEnvironmentVariable("RPC_BENCH_KCP_INTERVAL") is { } interval)
    kcpOptions.UpdateIntervalMilliseconds = int.Parse(interval);
if (Environment.GetEnvironmentVariable("RPC_BENCH_KCP_WINDOW") is { } window)
    kcpOptions.SendWindowSize = int.Parse(window);
#endif
var payload = new byte[size];
new Random(42).NextBytes(payload);
var serializer = new MemoryPackRpcSerializer();
var echo = new RpcMethod<byte[], byte[]>(1, 1);
var upload = new RpcMethod<byte[], int>(1, 2);
var download = new RpcMethod<int, byte[]>(1, 3);
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10));
var ct = deadline.Token;

if (role == "server")
{
    IAsyncDisposable listener;
    Func<ValueTask<ITransport>> accept;
    int port;
    if (transportName == "kcp")
    {
        var kcp = new KcpListener(0
#if KCP_OPTIONS
            , kcpOptions
#endif
        );
        listener = kcp;
        port = ((IPEndPoint)kcp.LocalEndPoint!).Port;
        accept = async () => (await kcp.AcceptAsync(ct)).Transport;
    }
    else
    {
        var tcp = new TcpConnectionAcceptor(0);
        listener = tcp;
        port = new Uri(tcp.ListenAddress).Port;
        accept = async () => (await tcp.AcceptAsync(ct)).Transport;
    }
    await using var ownedListener = listener;
    Emit(new { phase = "ready", port, environment = EnvironmentInfo() });
    var transport = wireTrace.Wrap(await accept());
    var registry = new RpcServiceRegistry();
    registry.Register(1, 1, (_, request, _) =>
    {
        wireTrace.Handled(request.RequestId);
        var value = serializer.Deserialize<byte[]>(request.Payload.Memory)!;
        return new ValueTask<RpcServerResponse>(RpcServerResponse.Serialize(request.RequestId, serializer, value));
    });
    registry.Register(1, 2, (_, request, _) =>
    {
        var value = serializer.Deserialize<byte[]>(request.Payload.Memory)!;
        if (!value.AsSpan().SequenceEqual(payload)) throw new InvalidDataException("Upload corrupt");
        return new ValueTask<RpcServerResponse>(RpcServerResponse.Serialize(request.RequestId, serializer, value.Length));
    });
    registry.Register(1, 3, (_, request, _) =>
    {
        if (serializer.Deserialize<int>(request.Payload.Memory) != size) throw new InvalidDataException("Download size");
        return new ValueTask<RpcServerResponse>(RpcServerResponse.Serialize(request.RequestId, serializer, payload));
    });
    await using var session = new RpcSession(transport, serializer, registry, "benchmark", true, RpcKeepAliveOptions.Disabled);
    await session.StartAsync(ct);
    Probe? probe = null;
    while (await Console.In.ReadLineAsync(ct) is { } line)
    {
        if (line == "begin") { probe = new Probe(); Emit(new { phase = "measuring" }); }
        else if (line == "end") Emit(new { phase = "resources", metrics = probe!.Finish() });
        else if (line == "stop") break;
    }
}
else
{
    var port = int.Parse(args[3]);
    var mode = args[4];
    var concurrency = int.Parse(args[5]);
    var seconds = double.Parse(args[6], System.Globalization.CultureInfo.InvariantCulture);
    var warmupSeconds = double.Parse(args[7], System.Globalization.CultureInfo.InvariantCulture);
    ITransport transport = transportName == "kcp" ? new KcpTransport("127.0.0.1", port
#if KCP_OPTIONS
        , kcpOptions
#endif
    ) : new TcpTransport("127.0.0.1", port);
    await using var client = new RpcClientRuntime(wireTrace.Wrap(transport), serializer, RpcKeepAliveOptions.Disabled);
    await client.StartAsync(ct);
    var warmup = Stopwatch.StartNew();
    var warmPayload = mode == "small" ? payload : new byte[256];
    await Task.WhenAll(Enumerable.Range(0, mode == "small" ? concurrency : 1).Select(async _ =>
    {
        do
        {
            var result = await client.CallAsync(echo, warmPayload, ct);
            if (!result.AsSpan().SequenceEqual(warmPayload)) throw new InvalidDataException("Warmup corrupt");
        } while (warmup.Elapsed.TotalSeconds < warmupSeconds);
    }));
    var histograms = Enumerable.Range(0, concurrency).Select(_ => new Histogram()).ToArray();
    Emit(new { phase = "warm", environment = EnvironmentInfo() });
    if (await Console.In.ReadLineAsync(ct) != "go") throw new InvalidDataException("Missing go");
    var probe = new Probe();
    var watch = Stopwatch.StartNew();
    int largeCount = int.Parse(Environment.GetEnvironmentVariable("RPC_BENCH_LARGE_COUNT") ?? "1");
    await Task.WhenAll(histograms.Select(async histogram =>
    {
        int calls = 0;
        do
        {
            var started = Stopwatch.GetTimestamp();
            if (mode == "upload")
            {
                if (await client.CallAsync(upload, payload, ct) != size) throw new InvalidDataException("Upload result");
            }
            else
            {
                var result = mode == "download"
                    ? await client.CallAsync(download, size, ct)
                    : await client.CallAsync(echo, payload, ct);
                if (!result.AsSpan().SequenceEqual(payload)) throw new InvalidDataException("Response corrupt");
            }
            histogram.Add(Stopwatch.GetElapsedTime(started).TotalMicroseconds);
        } while (mode == "small" ? watch.Elapsed.TotalSeconds < seconds : ++calls < largeCount);
    }));
    var elapsed = watch.Elapsed.TotalSeconds;
    var resources = probe.Finish();
    var histogram = Histogram.Merge(histograms);
    Emit(new { phase = "result", transport = transportName, mode, size, concurrency, elapsed,
#if KCP_OPTIONS
        kcpOptions = transportName == "kcp" ? kcpOptions : null,
#endif
        count = histogram.Count, rps = histogram.Count / elapsed,
        p50Us = histogram.Percentile(.50), p95Us = histogram.Percentile(.95), p99Us = histogram.Percentile(.99),
        maxUs = histogram.Maximum, resources, histogram = histogram.Buckets, errors = 0 });
}

static object EnvironmentInfo() => new
{
    runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
    arch = RuntimeInformation.ProcessArchitecture.ToString(), cpuCount = Environment.ProcessorCount,
    serverGC = GCSettings.IsServerGC, gcLatency = GCSettings.LatencyMode.ToString(),
    cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
    availableMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
};
static void Emit(object value) { Console.WriteLine(JsonSerializer.Serialize(value)); Console.Out.Flush(); }

// 1% logarithmic histogram in microseconds; no per-operation allocation or shared counter.
sealed class Histogram
{
    public long[] Buckets { get; } = new long[2400];
    public long Count;
    public double Maximum;
    public void Add(double us)
    {
        Buckets[Math.Clamp((int)Math.Ceiling(Math.Log(Math.Max(1, us)) * 100), 0, Buckets.Length - 1)]++;
        Count++;
        Maximum = Math.Max(Maximum, us);
    }
    public double Percentile(double p)
    {
        var target = (long)Math.Ceiling(Count * p);
        long sum = 0;
        for (var i = 0; i < Buckets.Length; i++) if ((sum += Buckets[i]) >= target) return Math.Exp(i / 100d);
        return Maximum;
    }
    public static Histogram Merge(IEnumerable<Histogram> histograms)
    {
        var merged = new Histogram();
        foreach (var h in histograms)
        {
            merged.Count += h.Count;
            merged.Maximum = Math.Max(merged.Maximum, h.Maximum);
            for (var i = 0; i < h.Buckets.Length; i++) merged.Buckets[i] += h.Buckets[i];
        }
        return merged;
    }
}

// Occupied managed memory includes garbage awaiting collection; it is not retained live bytes.
sealed class Probe
{
    private readonly Process process = Process.GetCurrentProcess();
    private readonly ManualResetEventSlim stop = new();
    private readonly Thread monitor;
    private readonly long allocated = GC.GetTotalAllocatedBytes(true);
    private readonly int[] collections = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
    private readonly double cpu;
    private readonly long[] start;
    private readonly long[] peak;
    public Probe()
    {
        start = Read(); peak = (long[])start.Clone();
        cpu = process.TotalProcessorTime.TotalSeconds;
        monitor = new Thread(() =>
        {
            while (!stop.Wait(20)) Sample();
        }) { IsBackground = true, Name = "BenchmarkResourceProbe" };
        monitor.Start();
    }
    private (long Heap, long Private, long Working) ReadValues()
    {
        if (OperatingSystem.IsWindows())
        {
            var counters = new ProcessMemoryCounters { Size = (uint)Marshal.SizeOf<ProcessMemoryCounters>() };
            if (!GetProcessMemoryInfo(process.Handle, out counters, counters.Size))
                throw new System.ComponentModel.Win32Exception();
            return (GC.GetTotalMemory(false), (long)counters.PrivateUsage, (long)counters.WorkingSetSize);
        }
        process.Refresh();
        return (GC.GetTotalMemory(false), process.PrivateMemorySize64, process.WorkingSet64);
    }
    private long[] Read()
    {
        var now = ReadValues();
        return [now.Heap, now.Private, now.Working];
    }
    private void Sample()
    {
        var now = ReadValues();
        peak[0] = Math.Max(peak[0], now.Heap);
        peak[1] = Math.Max(peak[1], now.Private);
        peak[2] = Math.Max(peak[2], now.Working);
    }
    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetProcessMemoryInfo(IntPtr process, out ProcessMemoryCounters counters, uint size);
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCounters
    {
        public uint Size, PageFaultCount;
        public nuint PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
            QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage;
    }
    public object Finish()
    {
        stop.Set(); monitor.Join(); Sample();
        var end = Read();
        var allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated;
        var cpuSeconds = process.TotalProcessorTime.TotalSeconds - cpu;
        var collectionCounts = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - collections[i]).ToArray();
        long? postGcManagedBytes = null;
        if (Environment.GetEnvironmentVariable("RPC_BENCH_RETAINED") == "1")
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            postGcManagedBytes = GC.GetTotalMemory(false);
        }
        var result = new { start, peak, end, allocatedBytes, cpuSeconds, collections = collectionCounts, postGcManagedBytes,
            diagnosticExceptions = ExceptionProbe.Snapshot(), diagnosticExceptionStacks = ExceptionProbe.Stacks() };
        stop.Dispose(); process.Dispose(); return result;
    }
}

// Opt-in diagnostic runs only: stack capture perturbs timings and is never enabled for A/B results.
static class ExceptionProbe
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> counts = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> stacks = new();
    public static void Start()
    {
        if (Environment.GetEnvironmentVariable("RPC_BENCH_DIAGNOSTICS") != "1") return;
        AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
        {
            if (e.Exception is not SemaphoreFullException) return;
            var stack = new StackTrace(1, false).ToString();
            var origin = stack.Contains("KcpFrameSender.Signal") ? "KcpFrameSender.Signal" :
                stack.Contains("KcpServerTransport") ? "KcpServerTransport" :
                stack.Contains("KcpTransport") ? "KcpTransport" : "other";
            counts.AddOrUpdate(origin, 1, (_, count) => count + 1);
            stacks.TryAdd(origin, stack);
        };
    }
    public static Dictionary<string, long> Snapshot() => new(counts);
    public static Dictionary<string, string> Stacks() => new(stacks);
}
