using System.Buffers.Binary;
using System.Collections.Concurrent;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Transport.Tcp;

namespace Lakona.Rpc.Tests;

public sealed class TcpRpcProgressTests
{
    [Theory]
    [InlineData(32, false)]
    [InlineData(128, true)]
    public async Task Concurrent_calls_preserve_identity_and_resume_after_idle(int concurrency, bool asynchronousHandler)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = deadline.Token;
        await using var listener = new TcpConnectionAcceptor(0);
        var address = new Uri(listener.ListenAddress);
        var serializer = new JsonRpcSerializer();
        await using var client = new RpcClientRuntime(new TcpTransport(address.Host, address.Port), serializer, RpcKeepAliveOptions.Disabled);
        // Exercise the production fallback dispatcher, not xUnit's synchronization context.
        client.SetDispatchSynchronizationContext(null);
        var connecting = client.StartAsync(ct).AsTask();
        var peer = (await listener.AcceptAsync(ct)).Transport;
        var requestIds = new ConcurrentDictionary<uint, byte>();
        var registry = new RpcServiceRegistry();
        registry.Register(1, 1, async (_, request, _) =>
        {
            if (!requestIds.TryAdd(request.RequestId, 0)) throw new InvalidOperationException("Duplicate request id.");
            if (asynchronousHandler) await Task.Yield();
            return RpcServerResponse.Encode(request.RequestId, RpcStatus.Ok, request.Payload.Memory);
        });
        await using var session = new RpcSession(peer, serializer, registry, "tcp-progress", true, RpcKeepAliveOptions.Disabled);
        await session.StartAsync(ct);
        await connecting;

        // Several bursts exercise the transition between an idle sender and queued writes.
        // Each worker owns a different payload, changed only after its response is consumed.
        for (var burst = 0; burst < 4; burst++)
        {
            await Task.WhenAll(Enumerable.Range(0, concurrency).Select(worker => Task.Run(async () =>
            {
                var payload = new byte[1024];
                new Random(worker).NextBytes(payload);
                for (var call = 0; call < 64; call++)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(payload, worker);
                    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), burst * 64 + call);
                    using var response = await client.CallRawAsync(1, 1, payload, ct);
                    Assert.True(response.Span.SequenceEqual(payload), $"Wrong response for worker {worker}, call {call}.");
                }
            }, ct))).WaitAsync(ct);
            await Task.Delay(10, ct);
        }
        Assert.Equal(concurrency * 256, requestIds.Count);
        Assert.Equal(RpcClientConnectionState.Connected, client.ConnectionState);
    }
}
