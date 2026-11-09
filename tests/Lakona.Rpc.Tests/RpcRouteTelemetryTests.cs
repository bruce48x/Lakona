using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Lakona.Rpc.Server.Observability;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Transport.Loopback;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lakona.Rpc.Tests;

[Collection(RpcTelemetryCollectionNames.Diagnostics)]
public sealed class RpcRouteTelemetryTests : IDisposable
{
    private readonly CancellationTokenSource Deadline = new(TimeSpan.FromSeconds(20));
    public void Dispose() => Deadline.Dispose();

    [Fact]
    public async Task Thousands_of_unknown_routes_share_one_category_and_keep_original_log_ids()
    {
        using var metrics = new Metrics();
        await using var fixture = new Fixture();
        fixture.Registry.Register(10, 20, (_, request, _) => Reply(request, RpcStatus.Ok));
        await fixture.StartAsync();
        const int count = 2000;
        for (var i = 0; i < count; i++)
        {
            // Include both unknown services and unknown methods on a known service.
            var service = i % 2 == 0 ? 1000 + i : 10;
            var method = 3000 + i;
            await fixture.SendAsync((uint)i + 1, service, method);
            using var response = await fixture.ReceiveAsync();
            Assert.Equal(RpcStatus.NotFound, response.Status);
            Assert.Equal((uint)i + 1, response.RequestId);
        }
        await metrics.WaitAsync("duration", count);
        var all = metrics.Items.ToArray();
        Assert.Equal(count * 4, all.Length);
        Assert.All(all, AssertUnknown);
        Assert.All(all.GroupBy(item => item.Name), group =>
            Assert.Single(group.Select(item => string.Join(";", item.Tags.Select(tag => $"{tag.Key}={tag.Value}"))).Distinct()));
        AssertLog(fixture, 1, 1000, 3000);
        AssertLog(fixture, count, 10, 3000 + count - 1);
    }

    [Theory]
    [InlineData(10, 20)]
    [InlineData(0, -1)]
    public async Task Registered_route_returning_not_found_retains_numeric_identity(int service, int method)
    {
        using var metrics = new Metrics();
        await using var fixture = new Fixture();
        fixture.Registry.Register(service, method, (_, request, _) => Reply(request, RpcStatus.NotFound));
        await fixture.StartAsync();
        await fixture.SendAsync(1, service, method);
        using var response = await fixture.ReceiveAsync();
        Assert.Equal(RpcStatus.NotFound, response.Status);
        await metrics.WaitAsync("duration", 1);
        Assert.Equal(4, metrics.Items.Count);
        Assert.All(metrics.Items, item => AssertKnown(item, service, method));
        Assert.Equal((int)RpcStatus.NotFound, metrics.Outcome().Tag("lakona.rpc.response.status_code"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Overloaded_request_keeps_its_admission_category(bool known)
    {
        using var metrics = new Metrics();
        var release = Signal();
        var entered = Signal();
        await using var fixture = new Fixture(new RpcServerLimits
        {
            MaxConcurrentRequestsPerSession = 1,
            MaxQueuedRequestsPerSession = 1
        });
        fixture.Registry.Register(10, 20, async (_, request, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return RpcServerResponse.Encode(request.RequestId, RpcStatus.Ok, ReadOnlyMemory<byte>.Empty);
        });
        await fixture.StartAsync();
        try
        {
            await fixture.SendAsync(1, 10, 20);
            await entered.Task.WaitAsync(Deadline.Token);
            await fixture.SendAsync(2, 10, 20);
            await metrics.WaitAsync("started", 2);
            await fixture.SendAsync(3, known ? 10 : 888, known ? 20 : 999);
            using var response = await fixture.ReceiveAsync();
            Assert.Equal(3u, response.RequestId);
            Assert.Equal(RpcStatus.Overloaded, response.Status);
            await metrics.WaitAsync("duration", 1);
            var terminal = metrics.Items.Where(item => item.Name.EndsWith("outcome") || item.Name.EndsWith("duration") && !item.Name.Contains("queue")).ToArray();
            Assert.Equal(2, terminal.Length);
            Assert.All(terminal, item =>
            {
                if (known) AssertKnown(item, 10, 20); else AssertUnknown(item);
                Assert.Equal((int)RpcStatus.Overloaded, item.Tag("lakona.rpc.response.status_code"));
            });
            AssertLog(fixture, 3, known ? 10 : 888, known ? 20 : 999);
        }
        finally { release.TrySetResult(); }
        using var first = await fixture.ReceiveAsync();
        using var second = await fixture.ReceiveAsync();
        Assert.Equal(RpcStatus.Ok, first.Status);
        Assert.Equal(RpcStatus.Ok, second.Status);
        await metrics.WaitAsync("duration", 3);
        Assert.Equal(3, metrics.Count("started"));
        Assert.Equal(3, metrics.Count("outcome"));
        Assert.Equal(2, metrics.Count("queue.duration"));
    }

    [Fact]
    public async Task Registration_while_queued_does_not_change_metric_identity_or_dispatch()
    {
        using var metrics = new Metrics();
        var release = Signal();
        var entered = Signal();
        await using var fixture = new Fixture(new RpcServerLimits { MaxConcurrentRequestsPerSession = 1 });
        fixture.Registry.Register(10, 20, async (_, request, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return RpcServerResponse.Encode(request.RequestId, RpcStatus.Ok, ReadOnlyMemory<byte>.Empty);
        });
        await fixture.StartAsync();
        try
        {
            await fixture.SendAsync(1, 10, 20);
            await entered.Task.WaitAsync(Deadline.Token);
            await fixture.SendAsync(2, 30, 40);
            await metrics.WaitAsync("started", 2);
            fixture.Registry.Register(30, 40, (_, request, _) => Reply(request, RpcStatus.Ok));
        }
        finally { release.TrySetResult(); }
        using var first = await fixture.ReceiveAsync();
        using var second = await fixture.ReceiveAsync();
        Assert.Equal(RpcStatus.Ok, first.Status);
        Assert.Equal(RpcStatus.Ok, second.Status);
        await metrics.WaitAsync("duration", 2);
        var unknown = metrics.Items.Where(item => item.Tag("lakona.rpc.route") is "unknown").ToArray();
        Assert.Equal(4, unknown.Length);
        Assert.All(unknown, AssertUnknown);
        Assert.All(metrics.Items.Except(unknown), item => AssertKnown(item, 10, 20));
        AssertLog(fixture, 2, 30, 40);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Cancellation_and_disconnect_keep_original_metric_category(bool known, bool disconnected)
    {
        using var metrics = new Metrics();
        await using var fixture = new Fixture(blockSend: true, failSend: disconnected);
        if (known) fixture.Registry.Register(10, 20, (_, request, _) => Reply(request, RpcStatus.Ok));
        await fixture.StartAsync();
        await fixture.SendAsync(1, 10, 20);
        await fixture.Transport.SendEntered.Task.WaitAsync(Deadline.Token);
        Task stop;
        try
        {
            if (disconnected)
            {
                await fixture.Client.DisposeAsync();
                stop = fixture.Server.WaitForCompletionAsync().AsTask();
            }
            else stop = fixture.Server.StopAsync().AsTask();
            await fixture.Transport.Canceled.Task.WaitAsync(Deadline.Token);
        }
        finally { fixture.Transport.ReleaseSend.TrySetResult(); }
        await stop.WaitAsync(Deadline.Token);
        await metrics.WaitAsync("duration", 1);
        Assert.Equal(4, metrics.Items.Count);
        Assert.All(metrics.Items, item => { if (known) AssertKnown(item, 10, 20); else AssertUnknown(item); });
        Assert.Equal(disconnected ? "connection_closed" : "canceled", metrics.Outcome().Tag("lakona.rpc.request.outcome"));
        AssertLog(fixture, 1, 10, 20);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_while_waiting_for_concurrency_keeps_category_and_log_ids(bool known)
    {
        using var metrics = new Metrics();
        var entered = Signal();
        await using var fixture = new Fixture(new RpcServerLimits { MaxConcurrentRequestsPerSession = 1 });
        fixture.Registry.Register(10, 20, async (_, request, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return RpcServerResponse.Encode(request.RequestId, RpcStatus.Ok, ReadOnlyMemory<byte>.Empty);
        });
        await fixture.StartAsync();
        await fixture.SendAsync(1, 10, 20);
        await entered.Task.WaitAsync(Deadline.Token);
        await fixture.SendAsync(2, known ? 10 : 999, known ? 20 : 888);
        await metrics.WaitAsync("started", 2);
        await fixture.Server.StopAsync();
        await metrics.WaitAsync("duration", 2);
        Assert.Equal(7, metrics.Items.Count);
        Assert.Equal(1, metrics.Count("queue.duration"));
        Assert.All(metrics.Items.Where(item => item.Name.EndsWith(".outcome")), item =>
            Assert.Equal("canceled", item.Tag("lakona.rpc.request.outcome")));
        if (known) Assert.All(metrics.Items, item => AssertKnown(item, 10, 20));
        else
        {
            var unknown = metrics.Items.Where(item => item.Tag("lakona.rpc.route") is "unknown").ToArray();
            Assert.Equal(3, unknown.Length);
            Assert.All(unknown, AssertUnknown);
        }
        AssertLog(fixture, 2, known ? 10 : 999, known ? 20 : 888);
    }

    private static ValueTask<RpcServerResponse> Reply(RpcRequestFrame request, RpcStatus status) =>
        new(RpcServerResponse.Encode(request.RequestId, status, ReadOnlyMemory<byte>.Empty));

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void AssertUnknown(Measurement item)
    {
        Assert.Equal("unknown", item.Tag("lakona.rpc.route"));
        Assert.Null(item.Tag("lakona.rpc.service.id"));
        Assert.Null(item.Tag("lakona.rpc.method.id"));
        Assert.Equal("lakona", item.Tag("rpc.system"));
    }

    private static void AssertKnown(Measurement item, int service, int method)
    {
        Assert.Equal(service, item.Tag("lakona.rpc.service.id"));
        Assert.Equal(method, item.Tag("lakona.rpc.method.id"));
        Assert.Null(item.Tag("lakona.rpc.route"));
    }

    private static void AssertLog(Fixture fixture, int request, int service, int method) =>
        Assert.Contains(fixture.Logs.Entries, entry =>
            entry.Any(p => p.Key == "RequestId" && Equals(p.Value, (uint)request)) &&
            entry.Any(p => p.Key == "ServiceId" && Equals(p.Value, service)) &&
            entry.Any(p => p.Key == "MethodId" && Equals(p.Value, method)));

    private sealed record Measurement(string Name, KeyValuePair<string, object?>[] Tags)
    {
        public object? Tag(string key) => Tags.FirstOrDefault(item => item.Key == key).Value;
    }

    private sealed class Metrics : IDisposable
    {
        private readonly CancellationTokenSource Deadline = new(TimeSpan.FromSeconds(20));
        private readonly MeterListener listener = new();
        private readonly Channel<bool> changed = Channel.CreateUnbounded<bool>();
        public ConcurrentQueue<Measurement> Items { get; } = new();
        public Metrics()
        {
            listener.InstrumentPublished = (instrument, current) =>
            {
                if (instrument.Meter.Name == LakonaRpcServerTelemetry.MeterName) current.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => Record(instrument, tags));
            listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => Record(instrument, tags));
            listener.Start();
        }
        private void Record(Instrument instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            Items.Enqueue(new(instrument.Name, tags.ToArray()));
            changed.Writer.TryWrite(true);
        }
        public int Count(string suffix) => Items.Count(item => item.Name == "lakona.rpc.server.request." + suffix);
        public Measurement Outcome() => Assert.Single(Items, item => item.Name.EndsWith(".outcome"));
        public async Task WaitAsync(string suffix, int count)
        {
            while (Count(suffix) < count) await changed.Reader.ReadAsync(Deadline.Token);
        }
        public void Dispose() { listener.Dispose(); Deadline.Dispose(); }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource Deadline = new(TimeSpan.FromSeconds(20));
        public ITransport Client { get; }
        public BlockingSendTransport Transport { get; }
        public RpcSession Server { get; }
        public RpcServiceRegistry Registry { get; } = new();
        public LogCollector Logs { get; } = new();
        public Fixture(RpcServerLimits? limits = null, bool blockSend = false, bool failSend = false)
        {
            LoopbackTransport.CreatePair(out var client, out var server);
            Client = client;
            Transport = new BlockingSendTransport(server, blockSend, failSend);
            Server = new RpcSession(Transport, new JsonRpcSerializer(), Registry, "route-metrics", true,
                requestLogger: Logs, limits: limits);
        }
        public async Task StartAsync()
        {
            await Client.ConnectAsync(Deadline.Token);
            await Server.StartAsync();
        }
        public async Task SendAsync(uint request, int service, int method) =>
            await Client.SendFrameAsync(RpcEnvelopeCodec.EncodeRequest(new RpcRequestEnvelope
            {
                RequestId = request, ServiceId = service, MethodId = method, Payload = ReadOnlyMemory<byte>.Empty
            }), Deadline.Token);
        public async Task<RpcResponseFrame> ReceiveAsync()
        {
            using var frame = await Client.ReceiveFrameAsync(Deadline.Token);
            return RpcEnvelopeCodec.DecodeResponse(frame);
        }
        public async ValueTask DisposeAsync()
        {
            Transport.ReleaseSend.TrySetResult();
            await Server.DisposeAsync();
            await Client.DisposeAsync();
            Deadline.Dispose();
        }
    }

    private sealed class BlockingSendTransport(ITransport inner, bool block, bool fail) : ITransport
    {
        public TaskCompletionSource SendEntered { get; } = Signal();
        public TaskCompletionSource Canceled { get; } = Signal();
        public TaskCompletionSource ReleaseSend { get; } = Signal();
        public bool IsConnected => inner.IsConnected;
        public ValueTask ConnectAsync(CancellationToken ct = default) => inner.ConnectAsync(ct);
        public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default) => inner.ReceiveFrameAsync(ct);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
        public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            if (block)
            {
                using var registration = ct.Register(() => Canceled.TrySetResult());
                SendEntered.TrySetResult();
                await ReleaseSend.Task;
                if (fail) throw new IOException("Peer disconnected while sending response.");
                ct.ThrowIfCancellationRequested();
            }
            await inner.SendFrameAsync(frame, ct);
        }
    }

    private sealed class LogCollector : ILogger
    {
        public ConcurrentQueue<KeyValuePair<string, object?>[]> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> properties) Entries.Enqueue(properties.ToArray());
        }
    }
}
