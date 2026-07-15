using System.Collections.Concurrent;
using System.Diagnostics;
using FrameworkBenchmark.Contracts;
using FrameworkBenchmark.Lakona.Contracts;
using FrameworkBenchmark.Lakona.Driver;
using FrameworkBenchmark.Lakona.Driver.Generated;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.WebSocket;

return await DriverProgram.RunAsync(args);

internal static class DriverProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var casePath = ReadOption(args, "--case");
            var resultPath = ReadOption(args, "--result");
            var command = BenchmarkJson.Read<CaseCommand>(casePath);
            if (command.Workload is not ("frontdoor.echo" or "cluster.direct" or "cluster.routed"))
            {
                throw new InvalidDataException($"Lakona driver does not support '{command.Workload}'.");
            }

            var endpoint = command.Endpoints.TryGetValue("client", out var value)
                ? value
                : throw new InvalidDataException("The case command does not contain the client endpoint.");
            var clients = await ConnectAsync(endpoint, command.ConnectionCount);
            try
            {
                var requestIds = new RequestIdSource();
                await RunWarmupAsync(clients, command, requestIds);
                var accumulator = new ResultAccumulator(command.Histogram);
                var measurementStart = Stopwatch.GetTimestamp();
                var measurementEnd = measurementStart + ToStopwatchTicks(command.Timing.MeasurementMilliseconds);
                await Task.WhenAll(clients.Select(client => RunMeasurementWorkerAsync(
                    client,
                    command,
                    requestIds,
                    accumulator,
                    measurementEnd)));

                var result = accumulator.CreateResult(command);
                BenchmarkJson.Write(resultPath, result);
                return 0;
            }
            finally
            {
                foreach (var client in clients)
                {
                    await client.DisposeAsync();
                }
            }
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.ToString());
            return 2;
        }
    }

    private static async Task<RpcClient[]> ConnectAsync(string endpoint, int count)
    {
        var clients = Enumerable.Range(0, count)
            .Select(_ => new RpcClient(new RpcClientOptions(
                new WsTransport(endpoint),
                new MemoryPackRpcSerializer())))
            .ToArray();
        try
        {
            await Task.WhenAll(clients.Select(client => client.ConnectAsync().AsTask()));
            return clients;
        }
        catch
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }

            throw;
        }
    }

    private static Task RunWarmupAsync(
        IReadOnlyList<RpcClient> clients,
        CaseCommand command,
        RequestIdSource requestIds)
    {
        if (command.Timing.WarmupMilliseconds == 0)
        {
            return Task.CompletedTask;
        }

        var end = Stopwatch.GetTimestamp() + ToStopwatchTicks(command.Timing.WarmupMilliseconds);
        return Task.WhenAll(clients.Select(client => RunWarmupWorkerAsync(client, command, requestIds, end)));
    }

    private static async Task RunWarmupWorkerAsync(
        RpcClient client,
        CaseCommand command,
        RequestIdSource requestIds,
        long end)
    {
        while (Stopwatch.GetTimestamp() < end)
        {
            var requestId = requestIds.Next();
            var payload = PayloadGenerator.Create(command.Seed, requestId, command.PayloadSize);
            var response = await CallAsync(client, command, requestId, payload);
            if (!IsValid(response, requestId, payload, ExpectedTerminalNode(command.Workload, requestId)))
            {
                throw new InvalidDataException("Lakona returned an invalid response during warm-up.");
            }
        }
    }

    private static async Task RunMeasurementWorkerAsync(
        RpcClient client,
        CaseCommand command,
        RequestIdSource requestIds,
        ResultAccumulator accumulator,
        long end)
    {
        while (Stopwatch.GetTimestamp() < end)
        {
            var requestId = requestIds.Next();
            var payload = PayloadGenerator.Create(command.Seed, requestId, command.PayloadSize);
            accumulator.Started();
            var started = Stopwatch.GetTimestamp();
            try
            {
                var response = await CallAsync(client, command, requestId, payload);
                accumulator.Completed(ToMicroseconds(Stopwatch.GetTimestamp() - started));
                switch (EchoResponseClassifier.Classify(
                    response,
                    requestId,
                    payload,
                    ExpectedTerminalNode(command.Workload, requestId)))
                {
                    case EchoResponseOutcome.Corrupt: accumulator.Corrupt(); break;
                    case EchoResponseOutcome.Misrouted: accumulator.Misrouted(); break;
                    case EchoResponseOutcome.Succeeded: accumulator.Succeeded(); break;
                }
            }
            catch (TimeoutException)
            {
                accumulator.TimedOut();
                break;
            }
            catch (RpcException)
            {
                accumulator.Completed(ToMicroseconds(Stopwatch.GetTimestamp() - started));
                accumulator.Rejected();
            }
            catch (Exception)
            {
                accumulator.Disconnected();
                break;
            }
        }
    }

    private static async Task<EchoResponse> CallAsync(
        RpcClient client,
        CaseCommand command,
        long requestId,
        byte[] payload)
    {
        var request = new EchoRequest
        {
            RequestId = requestId,
            Payload = payload,
            TargetKey = BenchmarkRouting.TargetKey(requestId)
        };
        var call = command.Workload switch
        {
            "cluster.direct" => client.Api.Benchmark.Echo.DirectAsync(request),
            "cluster.routed" => client.Api.Benchmark.Echo.RoutedAsync(request),
            _ => client.Api.Benchmark.Echo.EchoAsync(request)
        };
        return await call.AsTask().WaitAsync(
            TimeSpan.FromMilliseconds(command.Timing.RequestTimeoutMilliseconds));
    }

    private static string ExpectedTerminalNode(string workload, long requestId) => workload switch
    {
        "cluster.direct" => "worker-1",
        "cluster.routed" => BenchmarkRouting.Owner(BenchmarkRouting.TargetKey(requestId)),
        _ => "frontdoor-1"
    };

    private static bool IsValid(EchoResponse response, long requestId, byte[] payload, string terminalNode) =>
        response.RequestId == requestId &&
        response.TerminalNode == terminalNode &&
        response.Payload.AsSpan().SequenceEqual(payload);

    private static long ToStopwatchTicks(int milliseconds) =>
        checked((long)(milliseconds * (Stopwatch.Frequency / 1000d)));

    private static long ToMicroseconds(long ticks) =>
        Math.Max(1, (long)Math.Ceiling(ticks * (1_000_000d / Stopwatch.Frequency)));

    private static string ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length
            ? args[index + 1]
            : throw new ArgumentException($"{name} <path> is required.");
    }

    private sealed class RequestIdSource
    {
        private long current;

        public long Next() => Interlocked.Increment(ref current);
    }

    private sealed class ResultAccumulator
    {
        private readonly HistogramConfiguration configuration;
        private readonly ConcurrentDictionary<long, long> buckets = new();
        private long started;
        private long completed;
        private long succeeded;
        private long rejected;
        private long corrupt;
        private long misrouted;
        private long timedOut;
        private long disconnected;
        private long maximum;

        public ResultAccumulator(HistogramConfiguration configuration)
        {
            this.configuration = configuration;
        }

        public void Started() => Interlocked.Increment(ref started);
        public void Succeeded() => Interlocked.Increment(ref succeeded);
        public void Rejected() => Interlocked.Increment(ref rejected);
        public void Corrupt() => Interlocked.Increment(ref corrupt);
        public void Misrouted() => Interlocked.Increment(ref misrouted);
        public void TimedOut() => Interlocked.Increment(ref timedOut);
        public void Disconnected() => Interlocked.Increment(ref disconnected);

        public void Completed(long microseconds)
        {
            var value = Math.Clamp(
                microseconds,
                configuration.LowestDiscernibleValue,
                configuration.HighestTrackableValue);
            var upperBound = HistogramBucketQuantizer.UpperBound(value, configuration.SignificantDigits);
            buckets.AddOrUpdate(upperBound, 1, static (_, count) => count + 1);
            Interlocked.Increment(ref completed);
            InterlockedExtensions.Max(ref maximum, value);
        }

        public CaseResult CreateResult(CaseCommand command)
        {
            var outcomes = new CaseOutcomeCounts(
                started,
                completed,
                succeeded,
                rejected,
                corrupt,
                misrouted,
                timedOut,
                disconnected,
                0,
                0);
            var histogramBuckets = buckets
                .OrderBy(static pair => pair.Key)
                .Select(static pair => new HistogramBucket(pair.Key, pair.Value))
                .ToArray();
            var histogram = new LatencyHistogram(
                configuration.Unit,
                configuration.LowestDiscernibleValue,
                configuration.HighestTrackableValue,
                configuration.SignificantDigits,
                completed,
                maximum,
                histogramBuckets);
            return new CaseResult(
                BenchmarkSchemaVersions.V1,
                command.CaseId,
                command.Framework,
                command.Workload,
                succeeded / (command.Timing.MeasurementMilliseconds / 1000d),
                outcomes,
                histogram,
                new Dictionary<string, string>
                {
                    ["runtime"] = $".NET {Environment.Version}",
                    ["transport"] = "Lakona.Rpc WebSocket",
                    ["serializer"] = "MemoryPack 1.21.4",
                    ["clientLibrary"] = "Lakona.Rpc.Client 0.13.2",
                    ["connectionPolicy"] = "one persistent connection per outstanding slot"
                });
        }
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref long location, long value)
        {
            var current = Volatile.Read(ref location);
            while (current < value)
            {
                var observed = Interlocked.CompareExchange(ref location, value, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }
}
