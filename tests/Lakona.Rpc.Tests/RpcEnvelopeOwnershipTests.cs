using System.Reflection;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Tests;

public sealed class RpcEnvelopeOwnershipTests
{
    [Theory]
    [InlineData(RpcFrameType.Request)]
    [InlineData(RpcFrameType.Response)]
    [InlineData(RpcFrameType.Push)]
    public void TrailingBytes_DoNotRetainTheInputBuffer(RpcFrameType type)
    {
        using var valid = Encode(type);
        var malformed = new byte[valid.Length + 1];
        valid.CopyTo(malformed, 0);
        malformed[^1] = 0xFF;

        AssertDecodeFailureReleasesBuffer(malformed, frame => Decode(type, frame).Result);
    }

    public static TheoryData<byte[]> InvalidResponseSuffixes => new()
    {
        Array.Empty<byte>(),                     // Missing error flag.
        new byte[] { 1, 0, 0 },                  // Truncated error length.
        new byte[] { 1, 255, 255, 255, 255 },     // Negative error length.
        new byte[] { 1, 127, 255, 255, 255 },     // Error length exceeds the field limit.
        new byte[] { 1, 0, 0, 0, 2, 65 },        // Truncated error text.
        new byte[] { 1, 0, 0, 0, 1, 255 }        // Invalid UTF-8.
    };

    [Theory]
    [MemberData(nameof(InvalidResponseSuffixes))]
    public void InvalidResponseErrorField_DoesNotRetainTheInputBuffer(byte[] suffix)
    {
        using var valid = Encode(RpcFrameType.Response);
        // Replace the no-error flag following the nonempty payload.
        var prefixLength = valid.Length - 1;
        var malformed = new byte[prefixLength + suffix.Length];
        valid.Span[..prefixLength].CopyTo(malformed);
        suffix.CopyTo(malformed, prefixLength);

        AssertDecodeFailureReleasesBuffer(malformed, RpcEnvelopeCodec.DecodeResponse);
    }

    [Theory]
    [InlineData(RpcFrameType.Request)]
    [InlineData(RpcFrameType.Response)]
    [InlineData(RpcFrameType.Push)]
    public void SuccessfulDecode_OwnsPayloadUntilResultIsDisposed(RpcFrameType type)
    {
        using var input = Encode(type);
        var bufferReleased = ObserveBufferRelease(input);
        var (result, payload) = Decode(type, input);
        using (result)
        {
            input.Dispose();
            Assert.False(bufferReleased());
            Assert.Equal(new byte[] { 42 }, payload.ToArray());
        }
        Assert.True(bufferReleased());
    }

    private static void AssertDecodeFailureReleasesBuffer(byte[] bytes, Func<TransportFrame, IDisposable> decode)
    {
        using var input = TransportFrame.CopyOf(bytes);
        var bufferReleased = ObserveBufferRelease(input);
        Assert.Throws<InvalidOperationException>(() =>
        {
            using var unexpected = decode(input);
        });
        // The decoder borrows the input; failure must neither consume it nor retain a new lease.
        Assert.Equal(bytes, input.ToArray());
        input.Dispose();
        Assert.True(bufferReleased(), "Decoding failure retained the buffer after its caller released the input.");
    }

    private static Func<bool> ObserveBufferRelease(TransportFrame frame)
    {
        // There is no public pool-return observer. Keep this test-only inspection here,
        // checking synchronous ownership cleanup rather than waiting for GC/finalizers.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var owner = typeof(TransportFrame).GetField("_owner", flags)!.GetValue(frame)!;
        var buffer = owner.GetType().GetField("_buffer", flags)!;
        return () => buffer.GetValue(owner) is null;
    }

    private static TransportFrame Encode(RpcFrameType type) => type switch
    {
        RpcFrameType.Request => RpcEnvelopeCodec.EncodeRequest(new RpcRequestEnvelope
        {
            RequestId = 1, ServiceId = 2, MethodId = 3, Payload = new byte[] { 42 }
        }),
        RpcFrameType.Response => RpcEnvelopeCodec.EncodeResponse(1, RpcStatus.Ok, new byte[] { 42 }),
        RpcFrameType.Push => RpcEnvelopeCodec.EncodePush(new RpcPushEnvelope
        {
            ServiceId = 2, MethodId = 3, Payload = new byte[] { 42 },
            Metadata = new RpcPushMetadata { Type = "test", Payload = new byte[] { 7 } }
        }),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static (IDisposable Result, TransportFrame Payload) Decode(RpcFrameType type, TransportFrame input)
    {
        switch (type)
        {
            case RpcFrameType.Request:
                var request = RpcEnvelopeCodec.DecodeRequest(input);
                return (request, request.Payload);
            case RpcFrameType.Response:
                var response = RpcEnvelopeCodec.DecodeResponse(input);
                return (response, response.Payload);
            case RpcFrameType.Push:
                var push = RpcEnvelopeCodec.DecodePush(input);
                return (push, push.Payload);
            default:
                throw new ArgumentOutOfRangeException(nameof(type));
        }
    }
}
