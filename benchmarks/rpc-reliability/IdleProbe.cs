using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Lakona.Rpc.Transport.Kcp;
using Lakona.Rpc.Core;

static class IdleProbe
{
    // One process, many idle transport pairs and one active echo pair. Separate from RPC timings.
    public static async Task RunAsync(string[] args)
    {
        int connections = int.Parse(args[1]), interval = int.Parse(args[2]), window = int.Parse(args[3]);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = deadline.Token;
#if KCP_OPTIONS
        var options = new KcpTransportOptions { UpdateIntervalMilliseconds = interval, SendWindowSize = window };
        await using var listener = new KcpListener(0, options);
#else
        await using var listener = new KcpListener(0);
#endif
        var clients = new List<KcpTransport>();
        var servers = new List<ITransport>();
        try
        {
            for (int i = 0; i < connections; i++)
            {
                var client = new KcpTransport("127.0.0.1", ((IPEndPoint)listener.LocalEndPoint!).Port
#if KCP_OPTIONS
                    , options
#endif
                );
                clients.Add(client);
                await client.ConnectAsync(ct);
                servers.Add((await listener.AcceptAsync(ct)).Transport);
            }
            await Task.Delay(1000, ct);
            var idle = new Probe();
            await Task.Delay(5000, ct);
            var idleResources = idle.Finish();
            var serverLoop = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    using var frame = await servers[0].ReceiveFrameAsync(ct);
                    await servers[0].SendFrameAsync(frame.Memory, ct);
                }
            }, ct);
            var active = new Probe();
            var watch = Stopwatch.StartNew();
            var histogram = new Histogram();
            var payload = new byte[256];
            do
            {
                long start = Stopwatch.GetTimestamp();
                await clients[0].SendFrameAsync(payload, ct);
                using var reply = await clients[0].ReceiveFrameAsync(ct);
                if (!reply.Span.SequenceEqual(payload)) throw new InvalidDataException("Idle probe corrupt");
                histogram.Add(Stopwatch.GetElapsedTime(start).TotalMicroseconds);
            } while (watch.Elapsed.TotalSeconds < 5);
            var activeResources = active.Finish();
            deadline.Cancel();
            try { await serverLoop; } catch (OperationCanceledException) { }
            Console.WriteLine(JsonSerializer.Serialize(new { connections, interval, window, idleResources, activeResources,
                rps = histogram.Count / watch.Elapsed.TotalSeconds, p50Us = histogram.Percentile(.5), p99Us = histogram.Percentile(.99) }));
        }
        finally
        {
            foreach (var client in clients) await client.DisposeAsync();
            foreach (var server in servers) await server.DisposeAsync();
        }
    }
}
