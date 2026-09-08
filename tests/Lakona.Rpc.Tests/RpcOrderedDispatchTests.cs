using System.Net;
using System.Net.Sockets;
using Lakona.Rpc.Transport.Tcp;
using System.Collections.Concurrent;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Transport.Loopback;
using Lakona.Rpc.Server;

namespace Lakona.Rpc.Tests;

public class RpcOrderedDispatchTests
{
    [Fact]
    public Task AsyncAdmissionPreservesEntryOrderAndAllowsLaterRequestToFinishFirst() => Task.Run(async () =>
    {
        LoopbackTransport.CreatePair(out var transport, out var peer);
        var serializer = new JsonRpcSerializer();
        var gate = new PausedAdmissionGate();
        await using var client = new RpcClientRuntime(transport, serializer);
        await using var server = new RpcSession(peer, serializer, null, "ordered", true, requestGates: [gate]);
        var entries = new ConcurrentQueue<int>();
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Register(1, 1, async (request, ct) =>
        {
            entries.Enqueue(1);
            await releaseFirst.Task.WaitAsync(ct);
            return new RpcResponseEnvelope { RequestId = request.RequestId, Status = RpcStatus.Ok };
        });
        server.Register(1, 2, (request, _) =>
        {
            entries.Enqueue(2);
            return new ValueTask<RpcResponseEnvelope>(new RpcResponseEnvelope { RequestId = request.RequestId, Status = RpcStatus.Ok });
        });
        await server.StartAsync();
        await client.StartAsync();
        var first = client.CallAsync(new RpcMethod<int, RpcVoid>(1, 1), 0).AsTask();
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = client.CallAsync(new RpcMethod<int, RpcVoid>(1, 2), 0).AsTask();
        Assert.Empty(entries);
        gate.Release.SetResult();
        try
        {
            await second.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new[] { 1, 2 }, entries);
            Assert.False(first.IsCompleted);
        }
        finally { releaseFirst.TrySetResult(); }
        await first.WaitAsync(TimeSpan.FromSeconds(5));
    });

    private sealed class PausedAdmissionGate : IRpcSessionRequestGate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<RpcSessionRequestGateResult> EvaluateAsync(RpcSessionRequestGateContext context, CancellationToken cancellationToken = default)
        {
            if (context.MethodId == 1)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return RpcSessionRequestGateResult.Allow;
        }
    }

    [Fact]
    public Task PushCanAwaitRpcWhileLaterPushAndResponseProceed() => Task.Run(async () =>
    {
        LoopbackTransport.CreatePair(out var transport, out var peer);
        var serializer = new JsonRpcSerializer();
        await using var client = new RpcClientRuntime(transport, serializer);
        await using var server = new RpcSession(peer, serializer);
        var releaseResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new ConcurrentQueue<string>();
        server.Register(1, 1, async (request, ct) =>
        {
            await releaseResponse.Task.WaitAsync(ct);
            return new RpcResponseEnvelope { RequestId = request.RequestId, Status = RpcStatus.Ok };
        });
        client.RegisterNotificationHandler<int>(new RpcNotificationMethod<int>(1, 2), async value =>
        {
            if (value == 1)
            {
                events.Enqueue("P1-start");
                await client.CallAsync(new RpcMethod<int, RpcVoid>(1, 1), 0);
                events.Enqueue("P1-resumed");
                finished.TrySetResult();
            }
            else
            {
                events.Enqueue("P2");
                releaseResponse.TrySetResult();
            }
        });
        await server.StartAsync();
        await client.StartAsync();
        await server.SendNotificationAsync(1, 2, 1);
        await server.SendNotificationAsync(1, 2, 2);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "P1-start", "P2", "P1-resumed" }, events);
    });

    [Fact]
    public Task NotificationMayAwaitDisposalWithoutWaitingForItself() => Task.Run(async () =>
    {
        LoopbackTransport.CreatePair(out var transport, out var peer);
        var serializer = new JsonRpcSerializer();
        var client = new RpcClientRuntime(transport, serializer);
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.RegisterNotificationHandler<int>(new RpcNotificationMethod<int>(1, 2), async _ =>
        {
            await client.DisposeAsync();
            disposed.TrySetResult();
        });
        await peer.ConnectAsync();
        await client.StartAsync();
        using var payload = serializer.SerializeFrame(1);
        using var frame = RpcEnvelopeCodec.EncodePush(new RpcPushEnvelope { ServiceId = 1, MethodId = 2, Payload = payload.Memory });
        await peer.SendFrameAsync(frame.Memory);
        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await peer.DisposeAsync();
    });

    [Theory]
    [InlineData("typed", false, false)]
    [InlineData("raw", false, false)]
    [InlineData("void", false, false)]
    [InlineData("typed", true, false)]
    [InlineData("raw", true, false)]
    [InlineData("void", true, false)]
    [InlineData("typed", false, true)]
    [InlineData("raw", false, true)]
    [InlineData("void", false, true)]
    [InlineData("typed", true, true)]
    [InlineData("raw", true, true)]
    [InlineData("void", true, true)]
    public Task ResponseContinuation_EntersBetweenSurroundingPushes(string kind, bool tcp, bool hostContext) => RunWithContext(hostContext, async () =>
    {
        ITransport transport;
        ITransport peer;
        if (tcp)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            transport = new TcpTransport("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);
            var connecting = transport.ConnectAsync().AsTask();
            peer = new TcpServerTransport(await listener.AcceptTcpClientAsync());
            await connecting;
        }
        else
        {
            LoopbackTransport.CreatePair(out var first, out var second);
            transport = first;
            peer = second;
        }
        var serializer = new JsonRpcSerializer();
        await using var client = new RpcClientRuntime(transport, serializer);
        var entries = new ConcurrentQueue<string>();
        var lastPush = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.RegisterNotificationHandler<int>(new RpcNotificationMethod<int>(1, 2), value =>
        {
            entries.Enqueue("P" + value);
            if (value == 4) lastPush.TrySetResult();
            return default;
        });
        await peer.ConnectAsync();
        await client.StartAsync();

        async Task Observe()
        {
            if (kind == "raw")
            {
                using var result = await client.CallRawAsync(1, 1, ReadOnlyMemory<byte>.Empty);
            }
            else if (kind == "void")
                await RpcVoidTask.FromResult(client.CallAsync(new RpcMethod<int, RpcVoid>(1, 1), 0));
            else
                Assert.Equal(42, await client.CallAsync(new RpcMethod<int, int>(1, 1), 0));
            entries.Enqueue("R");
        }

        var observer = Observe();
        using var requestBytes = await peer.ReceiveFrameAsync();
        using var request = RpcEnvelopeCodec.DecodeRequest(requestBytes);
        async Task Push(int value)
        {
            using var payload = serializer.SerializeFrame(value);
            using var frame = RpcEnvelopeCodec.EncodePush(new RpcPushEnvelope
            {
                ServiceId = 1, MethodId = 2, Payload = payload.Memory
            });
            await peer.SendFrameAsync(frame.Memory);
        }
        await Push(1);
        await Push(2);
        using (var payload = serializer.SerializeFrame(42))
        using (var frame = RpcEnvelopeCodec.EncodeResponse(new RpcResponseEnvelope
        {
            RequestId = request.RequestId, Status = RpcStatus.Ok, Payload = payload.Memory
        })) await peer.SendFrameAsync(frame.Memory);
        await Push(3);
        await Push(4);
        await Task.WhenAll(observer, lastPush.Task).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "P1", "P2", "R", "P3", "P4" }, entries);
        await peer.DisposeAsync();
    });
    private static Task RunWithContext(bool enabled, Func<Task> action) => Task.Run(() =>
    {
        if (!enabled) return action();
        var context = new PumpContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var task = action();
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!task.IsCompleted)
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Host context stopped progressing.");
                if (!context.RunNext()) Thread.Sleep(1);
            }
            task.GetAwaiter().GetResult();
            return Task.CompletedTask;
        }
        finally { SynchronizationContext.SetSynchronizationContext(null); }
    });

    private sealed class PumpContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => _queue.Enqueue((callback, state));
        public bool RunNext()
        {
            if (!_queue.TryDequeue(out var work)) return false;
            work.Callback(work.State);
            return true;
        }
    }

}
