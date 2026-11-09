using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.MemoryPack;

const int operations = 1_000_000;
const int warmup = 100_000;
const int batch = 1_000;
var results = new List<object>();
foreach (var workers in new[] { 1, 8 })
foreach (var contactOnly in new[] { false, true })
for (var round = 0; round < 5; round++)
foreach (var baseline in round % 2 == 0 ? new[] { true, false } : new[] { false, true })
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var ct = timeout.Token;
    var transport = new NegotiationTransportFactory();
    var channel = new ClusterRpcChannel(transport, new MemoryPackRpcSerializer(), "cache.benchmark");
    IClusterClientFactory factory = baseline ? new BaselineClusterClientFactory(channel) : new ClusterClientFactory(channel);
    await using var lifetime = (IAsyncDisposable)factory;
    var target = new RouteLocation(new RouteKey("room/1"),
        new NodeReference(new ClusterIncarnationId(Guid.NewGuid()), new NodeId("worker"), new NodeIncarnationId(Guid.NewGuid())),
        new MembershipViewId(1), new NodeEndpoint("tcp://localhost:21002"));
    Func<ValueTask<IRpcClient>> lookup = contactOnly
        ? () => factory.GetClientAsync(target.Endpoint, ct)
        : () => factory.GetClientAsync(target, ct);
    var first = await lookup();
    using var ready = new CountdownEvent(workers);
    using var start = new ManualResetEventSlim();
    var tasks = Enumerable.Range(0, workers).Select(_ => Task.Factory.StartNew(() =>
    {
        var timings = new double[operations / workers / batch];
        try { for (var index = 0; index < warmup / workers; index++) Hit(); }
        finally { ready.Signal(); }
        start.Wait(ct);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < timings.Length; index++)
        {
            var timestamp = Stopwatch.GetTimestamp();
            for (var hit = 0; hit < batch; hit++) Hit();
            timings[index] = Stopwatch.GetElapsedTime(timestamp).TotalNanoseconds / batch;
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        return (timings, allocated);

        void Hit()
        {
            var pending = lookup();
            if (!pending.IsCompletedSuccessfully || !ReferenceEquals(first, pending.GetAwaiter().GetResult()))
                throw new InvalidOperationException("A warmed lookup did not synchronously reuse the client.");
        }
    }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
    ready.Wait(ct);
    using var process = Process.GetCurrentProcess();
    var cpuBefore = process.TotalProcessorTime;
    var gen0Before = GC.CollectionCount(0);
    var started = Stopwatch.GetTimestamp();
    start.Set();
    var measured = await Task.WhenAll(tasks).WaitAsync(ct);
    var elapsed = Stopwatch.GetElapsedTime(started);
    var cpu = process.TotalProcessorTime - cpuBefore;
    var gen0 = GC.CollectionCount(0) - gen0Before;
    var values = measured.SelectMany(value => value.timings).Order().ToArray();
    await lifetime.DisposeAsync();
    if (transport.Connections != 1 || transport.Disposals != 1)
        throw new InvalidOperationException("Connection creation/disposal count changed during cache hits.");
    var result = new
    {
        implementation = baseline ? "baseline" : "current", workers, contactOnly, round,
        operations, warmup, batch,
        elapsedMilliseconds = elapsed.TotalMilliseconds,
        hitsPerSecond = operations / elapsed.TotalSeconds,
        allocatedBytes = measured.Sum(value => value.allocated),
        bytesPerHit = measured.Sum(value => value.allocated) / (double)operations,
        cpuMilliseconds = cpu.TotalMilliseconds, gen0,
        batchMeanP50Nanoseconds = values[(int)Math.Ceiling(values.Length * .50) - 1],
        batchMeanP95Nanoseconds = values[(int)Math.Ceiling(values.Length * .95) - 1],
        batchMeanP99Nanoseconds = values[(int)Math.Ceiling(values.Length * .99) - 1],
        transport.Connections, transport.Disposals
    };
    results.Add(result);
    Console.WriteLine(JsonSerializer.Serialize(result));
}
File.WriteAllText(args[0], JsonSerializer.Serialize(new
{
    environment = new
    {
        runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
        logicalProcessors = Environment.ProcessorCount, serverGC = GCSettings.IsServerGC,
        tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), configuration = "Release"
    },
    results
}, new JsonSerializerOptions { WriteIndented = true }));

sealed class NegotiationTransportFactory : IClusterRpcTransport
{
    public int Connections;
    public int Disposals;
    public string Scheme => "tcp";
    public ValueTask<ITransport> ConnectAsync(ClusterEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Connections);
        return new ValueTask<ITransport>(new Connection(this));
    }
    public ValueTask<IRpcConnectionAcceptor> ListenAsync(ClusterEndpoint endpoint, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    private sealed class Connection(NegotiationTransportFactory owner) : ITransport
    {
        private byte[]? hello;
        private bool negotiated;
        private bool disposed;
        public bool IsConnected => !disposed;
        public ValueTask ConnectAsync(CancellationToken ct) => default;
        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            if (!negotiated) { hello = frame.ToArray(); hello[5] = 2; }
            return default;
        }
        public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct)
        {
            var reply = Interlocked.Exchange(ref hello, null);
            if (reply is not null)
            {
                negotiated = true;
                return TransportFrame.CopyOf(reply);
            }
            await Task.Delay(Timeout.Infinite, ct);
            return TransportFrame.Empty;
        }
        public ValueTask DisposeAsync()
        {
            disposed = true;
            Interlocked.Increment(ref owner.Disposals);
            return default;
        }
    }
}
