using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Transport.Loopback;
using Xunit;

namespace Lakona.Rpc.Tests;

public sealed class RpcClientRuntimeRawCallTests
{
    [Fact]
    public async Task CallRawAsync_sends_payload_without_serializer()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        await using var client = new RpcClientRuntime(clientTransport, new ThrowingSerializer());
        await using var server = serverTransport;
        await client.StartAsync();

        var serviceId = -1;
        var methodId = -1;
        byte[]? requestPayload = null;
        var serverTask = Task.Run(async () =>
        {
            await server.ConnectAsync();
            using var requestFrame = await server.ReceiveFrameAsync();
            using var request = RpcEnvelopeCodec.DecodeRequest(requestFrame);
            serviceId = request.ServiceId;
            methodId = request.MethodId;
            requestPayload = request.Payload.Memory.ToArray();
            using var response = RpcEnvelopeCodec.EncodeResponse(request.RequestId, RpcStatus.Ok, new byte[] { 4, 5 });
            await server.SendFrameAsync(response.Memory);
        });

        using var responsePayload = await client.CallRawAsync(
            0,
            1,
            new byte[] { 1, 2, 3 },
            default).AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(new byte[] { 4, 5 }, responsePayload.Memory.ToArray());
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, serviceId);
        Assert.Equal(1, methodId);
        Assert.Equal(new byte[] { 1, 2, 3 }, requestPayload);
    }

    private sealed class ThrowingSerializer : IRpcSerializer
    {
        public TransportFrame SerializeFrame<T>(T value) => throw new InvalidOperationException("Serializer must not be used.");

        public T Deserialize<T>(ReadOnlySpan<byte> data) => throw new InvalidOperationException("Serializer must not be used.");

        public T Deserialize<T>(ReadOnlyMemory<byte> data) => throw new InvalidOperationException("Serializer must not be used.");
    }
}
