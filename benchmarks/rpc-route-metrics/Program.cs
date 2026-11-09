using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Lakona.Rpc.Server.Observability;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Transport.Loopback;

var results = new List<Result>();
var registry = new RpcServiceRegistry();
registry.Register(17, 23, (_, request, _) => new ValueTask<RpcServerResponse>(
    RpcServerResponse.Encode(request.RequestId, RpcStatus.Ok, request.Payload)));
foreach (var enabled in new[] { false, true })
{
    using var listener = new MeterListener();
    if (enabled)
    {
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == LakonaRpcServerTelemetry.MeterName)
                current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, _, _) => { });
        listener.SetMeasurementEventCallback<double>((_, _, _, _) => { });
        listener.Start();
    }
    Record(100_000);
    for (var round = 0; round < 5; round++)
    {
        const int count = 500_000;
        GC.Collect();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        Record(count);
        results.Add(new("metrics", enabled, 0, round, count,
            Stopwatch.GetElapsedTime(start).TotalNanoseconds / count,
            (GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)count));
    }
    foreach (var window in new[] { 1, 8 })
    {
        await RoundTrips(4_000, window); // Warm pools and dispatch paths.
        for (var round = 0; round < 3; round++)
        {
            var result = await RoundTrips(32_000, window);
            results.Add(result with { Enabled = enabled, Round = round });
        }
    }
}
File.WriteAllText(args[0], JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
foreach (var group in results.GroupBy(r => (r.Mode, r.Enabled, r.Window)))
    Console.WriteLine($"{group.Key}: {group.Average(r => r.NsPerRequest):F1} ns/request, {group.Average(r => r.BytesPerRequest):F1} B/request");

void Record(int count)
{
    for (var i = 0; i < count; i++)
    {
#if OPS_BASELINE
        RpcServerTelemetry.RecordRequestStarted(17, 23);
        RpcServerTelemetry.RecordRequestQueueDuration(17, 23, TimeSpan.Zero);
        RpcServerTelemetry.RecordRequestOutcome(17, 23, "response", RpcStatus.Ok, TimeSpan.Zero);
#else
        registry.TryGetDescriptor(17, 23, out var route);
        RpcServerTelemetry.RecordRequestStarted(route);
        RpcServerTelemetry.RecordRequestQueueDuration(route, TimeSpan.Zero);
        RpcServerTelemetry.RecordRequestOutcome(route, "response", RpcStatus.Ok, TimeSpan.Zero);
#endif
    }
}

async Task<Result> RoundTrips(int count, int window)
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
    LoopbackTransport.CreatePair(out var client, out var transport);
    await using var peer = client;
    await using var session = new RpcSession(transport, new JsonRpcSerializer(), registry, "benchmark", true);
    await client.ConnectAsync(deadline.Token);
    await session.StartAsync();
    var payload = new byte[32];
    for (var i = 0; i < payload.Length; i++) payload[i] = (byte)i;
    GC.Collect();
    var allocated = GC.GetTotalAllocatedBytes(true);
    var start = Stopwatch.GetTimestamp();
    for (var batch = 0; batch < count; batch += window)
    {
        for (var offset = 0; offset < window; offset++)
            await client.SendFrameAsync(RpcEnvelopeCodec.EncodeRequest(new RpcRequestEnvelope
            {
                RequestId = (uint)(batch + offset + 1), ServiceId = 17, MethodId = 23, Payload = payload
            }), deadline.Token);
        for (var offset = 0; offset < window; offset++)
        {
            using var frame = await client.ReceiveFrameAsync(deadline.Token);
            using var response = RpcEnvelopeCodec.DecodeResponse(frame);
            if (response.Status != RpcStatus.Ok || !response.Payload.Span.SequenceEqual(payload))
                throw new InvalidOperationException("Invalid RPC response.");
        }
    }
    // Include the final dispatch cleanup; otherwise the last response may arrive
    // before its terminal metrics and budget release have finished.
    await session.StopAsync();
    return new("loopback32", false, window, 0, count,
        Stopwatch.GetElapsedTime(start).TotalNanoseconds / count,
        (GC.GetTotalAllocatedBytes(true) - allocated) / (double)count);
}

record Result(string Mode, bool Enabled, int Window, int Round, int Count, double NsPerRequest, double BytesPerRequest);
