using System.Buffers.Binary;
using System.Text.Json;
using Lakona.Rpc.Core;

// Opt-in diagnosis of a stalled run, not a performance measurement.
sealed class WireTrace : IDisposable
{
    private readonly string role;
    private readonly object gate = new();
    private readonly Entry[] recent = new Entry[256];
    private readonly Dictionary<string, Counter> counters = new();
    private readonly CancellationTokenSource stop = new();
    private readonly Task? monitor;
    private long sequence;
    private readonly bool enabled = Environment.GetEnvironmentVariable("RPC_BENCH_WIRE_TRACE") == "1";

    public WireTrace(string role)
    {
        this.role = role;
        if (enabled) monitor = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    await Task.Delay(2000, stop.Token);
                    WriteSnapshot();
                }
            }
            catch (OperationCanceledException) { }
        });
    }
    public ITransport Wrap(ITransport transport) => enabled ? new TracedTransport(transport, this) : transport;
    public void Handled(uint id) { if (enabled) Record("handled", 0, id, 0); }
    private void Record(string stage, byte kind, uint id, int length)
    {
        lock (gate)
        {
            if (!counters.TryGetValue(stage, out var count)) counters.Add(stage, count = new Counter());
            count.Count++; count.IdSum += id; count.LastId = id;
            var serial = sequence++;
            recent[serial % recent.Length] = new Entry(serial, stage, kind, id, length);
        }
    }
    private static (byte Kind, uint Id, int Length) Header(ReadOnlyMemory<byte> frame) =>
        (frame.Span[0], BinaryPrimitives.ReadUInt32BigEndian(frame.Span.Slice(1, 4)), frame.Length);
    private void WriteSnapshot()
    {
        string json;
        lock (gate) json = JsonSerializer.Serialize(new { wire = role, sequence, counters,
            recent = recent.Where(e => e.Stage is not null).OrderBy(e => e.Sequence).ToArray() });
        Console.Error.WriteLine(json);
    }
    public void Dispose()
    {
        if (enabled) { stop.Cancel(); monitor!.GetAwaiter().GetResult(); WriteSnapshot(); }
        stop.Dispose();
    }
    private sealed class Counter
    {
        public long Count { get; set; }
        public long IdSum { get; set; }
        public uint LastId { get; set; }
    }
    private readonly record struct Entry(long Sequence, string? Stage, byte Kind, uint Id, int Length);
    private sealed class TracedTransport(ITransport inner, WireTrace trace) : ITransport
    {
        public bool IsConnected => inner.IsConnected;
        public ValueTask ConnectAsync(CancellationToken ct = default) => inner.ConnectAsync(ct);
        public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            var header = Header(frame);
            trace.Record("send-start", header.Kind, header.Id, header.Length);
            await inner.SendFrameAsync(frame, ct).ConfigureAwait(false);
            if (Header(frame) != header) throw new InvalidDataException("Outbound frame header changed before send completion.");
            trace.Record("send-complete", header.Kind, header.Id, header.Length);
        }
        public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            var frame = await inner.ReceiveFrameAsync(ct).ConfigureAwait(false);
            if (!frame.IsEmpty)
            {
                var header = Header(frame.Memory);
                trace.Record("receive", header.Kind, header.Id, header.Length);
            }
            return frame;
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
