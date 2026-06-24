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
        await client.StartAsync();

        var serverTask = Task.Run(async () =>
        {
            await serverTransport.ConnectAsync();
            using var requestFrame = await serverTransport.ReceiveFrameAsync();
            var request = RpcEnvelopeCodec.DecodeRequest(requestFrame);
            Assert.Equal(0, request.ServiceId);
            Assert.Equal(1, request.MethodId);
            Assert.Equal(new byte[] { 1, 2, 3 }, request.Payload.Memory.ToArray());
            using var response = RpcEnvelopeCodec.EncodeResponse(request.RequestId, RpcStatus.Ok, new byte[] { 4, 5 });
            await serverTransport.SendFrameAsync(response.Memory);
        });

        using var responsePayload = await client.CallRawAsync(
            0,
            1,
            new byte[] { 1, 2, 3 },
            default);

        Assert.Equal(new byte[] { 4, 5 }, responsePayload.Memory.ToArray());
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class ThrowingSerializer : IRpcSerializer
    {
        public TransportFrame SerializeFrame<T>(T value) => throw new InvalidOperationException("Serializer must not be used.");

        public T Deserialize<T>(ReadOnlySpan<byte> data) => throw new InvalidOperationException("Serializer must not be used.");

        public T Deserialize<T>(ReadOnlyMemory<byte> data) => throw new InvalidOperationException("Serializer must not be used.");
    }
}
