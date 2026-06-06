using System.Buffers.Binary;
using System.Text;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Tests;

public class RpcEnvelopeCodecTests
{
    [Fact]
    public void RequestRoundTrip_PreservesAllFields()
    {
        var original = new RpcRequestEnvelope
        {
            RequestId = 42,
            ServiceId = 7,
            MethodId = 3,
            Payload = new byte[] { 1, 2, 3, 4, 5 }
        };

        using var encoded = RpcEnvelopeCodec.EncodeRequest(original);
        using var decoded = RpcEnvelopeCodec.DecodeRequest(encoded);

        Assert.Equal(original.RequestId, decoded.RequestId);
        Assert.Equal(original.ServiceId, decoded.ServiceId);
        Assert.Equal(original.MethodId, decoded.MethodId);
        Assert.Equal(original.Payload.ToArray(), decoded.Payload.ToArray());
    }

    [Fact]
    public void EncodeRequest_MatchesWireProtocolV1GoldenBytes()
    {
        var request = new RpcRequestEnvelope
        {
            RequestId = 1,
            ServiceId = 2,
            MethodId = 3,
            Payload = new byte[] { 0xAA, 0xBB, 0xCC }
        };

        using var encoded = RpcEnvelopeCodec.EncodeRequest(request);

        Assert.Equal(new byte[]
        {
            0x01,
            0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x02,
            0x00, 0x00, 0x00, 0x03,
            0x00, 0x00, 0x00, 0x03,
            0xAA, 0xBB, 0xCC
        }, encoded.ToArray());
    }

    [Fact]
    public void DecodeRequest_ReadsWireProtocolV1GoldenBytes()
    {
        using var frame = TransportFrame.CopyOf(new byte[]
        {
            0x01,
            0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x02,
            0x00, 0x00, 0x00, 0x03,
            0x00, 0x00, 0x00, 0x03,
            0xAA, 0xBB, 0xCC
        });

        using var decoded = RpcEnvelopeCodec.DecodeRequest(frame);

        Assert.Equal(1u, decoded.RequestId);
        Assert.Equal(2, decoded.ServiceId);
        Assert.Equal(3, decoded.MethodId);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, decoded.Payload.ToArray());
    }

    [Fact]
    public void ResponseRoundTrip_PreservesAllFields()
    {
        var original = new RpcResponseEnvelope
        {
            RequestId = 99,
            Status = RpcStatus.Ok,
            Payload = new byte[] { 10, 20 },
            ErrorMessage = null
        };

        using var encoded = RpcEnvelopeCodec.EncodeResponse(original);
        using var decoded = RpcEnvelopeCodec.DecodeResponse(encoded);

        Assert.Equal(original.RequestId, decoded.RequestId);
        Assert.Equal(original.Status, decoded.Status);
        Assert.Equal(original.Payload.ToArray(), decoded.Payload.ToArray());
        Assert.Null(decoded.ErrorMessage);
    }

    [Fact]
    public void EncodeResponseOk_MatchesWireProtocolV1GoldenBytes()
    {
        var response = new RpcResponseEnvelope
        {
            RequestId = 0x01020304,
            Status = RpcStatus.Ok,
            Payload = new byte[] { 0x10, 0x20 }
        };

        using var encoded = RpcEnvelopeCodec.EncodeResponse(response);

        Assert.Equal(new byte[]
        {
            0x02,
            0x01, 0x02, 0x03, 0x04,
            0x00,
            0x00, 0x00, 0x00, 0x02,
            0x10, 0x20,
            0x00
        }, encoded.ToArray());
    }

    [Fact]
    public void DecodeResponseOk_ReadsWireProtocolV1GoldenBytes()
    {
        using var frame = TransportFrame.CopyOf(new byte[]
        {
            0x02,
            0x01, 0x02, 0x03, 0x04,
            0x00,
            0x00, 0x00, 0x00, 0x02,
            0x10, 0x20,
            0x00
        });

        using var decoded = RpcEnvelopeCodec.DecodeResponse(frame);

        Assert.Equal(0x01020304u, decoded.RequestId);
        Assert.Equal(RpcStatus.Ok, decoded.Status);
        Assert.Equal(new byte[] { 0x10, 0x20 }, decoded.Payload.ToArray());
        Assert.Null(decoded.ErrorMessage);
    }

    [Fact]
    public void ResponseRoundTrip_WithErrorMessage()
    {
        var original = new RpcResponseEnvelope
        {
            RequestId = 1,
            Status = RpcStatus.HandlerError,
            Payload = Array.Empty<byte>(),
            ErrorMessage = "something went wrong"
        };

        using var encoded = RpcEnvelopeCodec.EncodeResponse(original);
        using var decoded = RpcEnvelopeCodec.DecodeResponse(encoded);

        Assert.Equal(RpcStatus.HandlerError, decoded.Status);
        Assert.Equal("something went wrong", decoded.ErrorMessage);
    }

    [Fact]
    public void EncodeResponseError_MatchesWireProtocolV1GoldenBytes()
    {
        var response = new RpcResponseEnvelope
        {
            RequestId = 7,
            Status = RpcStatus.HandlerError,
            Payload = Array.Empty<byte>(),
            ErrorMessage = "fail"
        };

        using var encoded = RpcEnvelopeCodec.EncodeResponse(response);

        Assert.Equal(new byte[]
        {
            0x02,
            0x00, 0x00, 0x00, 0x07,
            0x02,
            0x00, 0x00, 0x00, 0x00,
            0x01,
            0x00, 0x00, 0x00, 0x04,
            0x66, 0x61, 0x69, 0x6C
        }, encoded.ToArray());
    }

    [Fact]
    public void DecodeResponseError_ReadsWireProtocolV1GoldenBytes()
    {
        using var frame = TransportFrame.CopyOf(new byte[]
        {
            0x02,
            0x00, 0x00, 0x00, 0x07,
            0x02,
            0x00, 0x00, 0x00, 0x00,
            0x01,
            0x00, 0x00, 0x00, 0x04,
            0x66, 0x61, 0x69, 0x6C
        });

        using var decoded = RpcEnvelopeCodec.DecodeResponse(frame);

        Assert.Equal(7u, decoded.RequestId);
        Assert.Equal(RpcStatus.HandlerError, decoded.Status);
        Assert.True(decoded.Payload.IsEmpty);
        Assert.Equal("fail", decoded.ErrorMessage);
    }

    [Fact]
    public void EncodeRequest_NullArg_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RpcEnvelopeCodec.EncodeRequest(null!));
    }

    [Fact]
    public void EncodeResponse_NullArg_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RpcEnvelopeCodec.EncodeResponse(null!));
    }

    [Fact]
    public void DecodeRequest_TruncatedData_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            using var frame = TransportFrame.CopyOf(new byte[] { 0, 0 });
            using var _ = RpcEnvelopeCodec.DecodeRequest(frame);
        });
    }

    [Fact]
    public void DecodeResponse_TruncatedData_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            using var frame = TransportFrame.CopyOf(new byte[] { 0, 0 });
            using var _ = RpcEnvelopeCodec.DecodeResponse(frame);
        });
    }

    [Fact]
    public void DecodeRequest_TrailingBytes_Throws()
    {
        var req = new RpcRequestEnvelope
        {
            RequestId = 1, ServiceId = 1, MethodId = 1,
            Payload = new byte[] { 42 }
        };
        using var valid = RpcEnvelopeCodec.EncodeRequest(req);
        var withExtra = new byte[valid.Length + 1];
        valid.CopyTo(withExtra, 0);
        withExtra[^1] = 0xFF;

        Assert.Throws<InvalidOperationException>(() =>
        {
            using var frame = TransportFrame.CopyOf(withExtra);
            using var _ = RpcEnvelopeCodec.DecodeRequest(frame);
        });
    }

    [Fact]
    public void DecodeResponse_TrailingBytes_Throws()
    {
        var resp = new RpcResponseEnvelope
        {
            RequestId = 1, Status = RpcStatus.Ok,
            Payload = new byte[] { 42 }
        };
        using var valid = RpcEnvelopeCodec.EncodeResponse(resp);
        var withExtra = new byte[valid.Length + 1];
        valid.CopyTo(withExtra, 0);
        withExtra[^1] = 0xFF;

        Assert.Throws<InvalidOperationException>(() =>
        {
            using var frame = TransportFrame.CopyOf(withExtra);
            using var _ = RpcEnvelopeCodec.DecodeResponse(frame);
        });
    }

    [Fact]
    public void DecodeRequest_NegativePayloadLength_Throws()
    {
        var data = new byte[1 + 4 + 4 + 4 + 4];
        var offset = 0;
        data[offset++] = (byte)RpcFrameType.Request;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), 1); offset += 4;
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset), 1); offset += 4;
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset), 1); offset += 4;
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset), -1);

        Assert.Throws<InvalidOperationException>(() =>
        {
            using var frame = TransportFrame.CopyOf(data);
            using var _ = RpcEnvelopeCodec.DecodeRequest(frame);
        });
    }

    [Fact]
    public void DecodeRequest_OversizedPayloadLength_Throws()
    {
        var payloadLen = RpcEnvelopeCodec.MaxPayloadSize + 1;
        var data = new byte[1 + 4 + 4 + 4 + 4];
        var offset = 0;
        data[offset++] = (byte)RpcFrameType.Request;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), 1); offset += 4;
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset), 1); offset += 4;
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset), 1); offset += 4;
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset), payloadLen);

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var frame = TransportFrame.CopyOf(data);
            using var _ = RpcEnvelopeCodec.DecodeRequest(frame);
        });
        Assert.Contains("invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DecodeResponse_OversizedPayloadLength_Throws()
    {
        var payloadLen = RpcEnvelopeCodec.MaxPayloadSize + 1;
        var data = new byte[1 + 4 + 1 + 4];
        var offset = 0;
        data[offset++] = (byte)RpcFrameType.Response;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), 1); offset += 4;
        data[offset++] = 0;
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset), payloadLen);

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var frame = TransportFrame.CopyOf(data);
            using var _ = RpcEnvelopeCodec.DecodeResponse(frame);
        });
        Assert.Contains("invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PushRoundTrip_PreservesAllFields()
    {
        var original = new RpcPushEnvelope
        {
            ServiceId = 5,
            MethodId = 3,
            Payload = new byte[] { 10, 20, 30 }
        };

        using var encoded = RpcEnvelopeCodec.EncodePush(original);
        using var decoded = RpcEnvelopeCodec.DecodePush(encoded);

        Assert.Equal(original.ServiceId, decoded.ServiceId);
        Assert.Equal(original.MethodId, decoded.MethodId);
        Assert.Equal(original.Payload.ToArray(), decoded.Payload.ToArray());
    }

    [Fact]
    public void EncodePush_MatchesWireProtocolV1GoldenBytes()
    {
        var push = new RpcPushEnvelope
        {
            ServiceId = 2,
            MethodId = 3,
            Payload = new byte[] { 0xAA, 0xBB }
        };

        using var encoded = RpcEnvelopeCodec.EncodePush(push);

        Assert.Equal(new byte[]
        {
            0x03,
            0x00, 0x00, 0x00, 0x02,
            0x00, 0x00, 0x00, 0x03,
            0x00, 0x00, 0x00, 0x02,
            0xAA, 0xBB
        }, encoded.ToArray());
    }

    [Fact]
    public void DecodePush_ReadsWireProtocolV1GoldenBytes()
    {
        using var frame = TransportFrame.CopyOf(new byte[]
        {
            0x03,
            0x00, 0x00, 0x00, 0x02,
            0x00, 0x00, 0x00, 0x03,
            0x00, 0x00, 0x00, 0x02,
            0xAA, 0xBB
        });

        using var decoded = RpcEnvelopeCodec.DecodePush(frame);

        Assert.Equal(2, decoded.ServiceId);
        Assert.Equal(3, decoded.MethodId);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, decoded.Payload.ToArray());
    }

    [Fact]
    public void KeepAlivePingRoundTrip_PreservesTimestamp()
    {
        var original = new RpcKeepAlivePingEnvelope
        {
            TimestampTicksUtc = DateTimeOffset.UtcNow.UtcTicks
        };

        using var encoded = RpcEnvelopeCodec.EncodeKeepAlivePing(original);
        var decoded = RpcEnvelopeCodec.DecodeKeepAlivePing(encoded.Span);

        Assert.Equal(original.TimestampTicksUtc, decoded.TimestampTicksUtc);
    }

    [Fact]
    public void EncodeKeepAlivePing_MatchesWireProtocolV1GoldenBytes()
    {
        var ping = new RpcKeepAlivePingEnvelope
        {
            TimestampTicksUtc = 0x0102030405060708L
        };

        using var encoded = RpcEnvelopeCodec.EncodeKeepAlivePing(ping);

        Assert.Equal(new byte[]
        {
            0x04,
            0x01, 0x02, 0x03, 0x04,
            0x05, 0x06, 0x07, 0x08
        }, encoded.ToArray());
    }

    [Fact]
    public void DecodeKeepAlivePing_ReadsWireProtocolV1GoldenBytes()
    {
        var decoded = RpcEnvelopeCodec.DecodeKeepAlivePing(new byte[]
        {
            0x04,
            0x01, 0x02, 0x03, 0x04,
            0x05, 0x06, 0x07, 0x08
        });

        Assert.Equal(0x0102030405060708L, decoded.TimestampTicksUtc);
    }

    [Fact]
    public void KeepAlivePongRoundTrip_PreservesTimestamp()
    {
        var original = new RpcKeepAlivePongEnvelope
        {
            TimestampTicksUtc = DateTimeOffset.UtcNow.UtcTicks
        };

        using var encoded = RpcEnvelopeCodec.EncodeKeepAlivePong(original);
        var decoded = RpcEnvelopeCodec.DecodeKeepAlivePong(encoded.Span);

        Assert.Equal(original.TimestampTicksUtc, decoded.TimestampTicksUtc);
    }

    [Fact]
    public void EncodeKeepAlivePong_MatchesWireProtocolV1GoldenBytes()
    {
        var pong = new RpcKeepAlivePongEnvelope
        {
            TimestampTicksUtc = 0x1122334455667788L
        };

        using var encoded = RpcEnvelopeCodec.EncodeKeepAlivePong(pong);

        Assert.Equal(new byte[]
        {
            0x05,
            0x11, 0x22, 0x33, 0x44,
            0x55, 0x66, 0x77, 0x88
        }, encoded.ToArray());
    }

    [Fact]
    public void DecodeKeepAlivePong_ReadsWireProtocolV1GoldenBytes()
    {
        var decoded = RpcEnvelopeCodec.DecodeKeepAlivePong(new byte[]
        {
            0x05,
            0x11, 0x22, 0x33, 0x44,
            0x55, 0x66, 0x77, 0x88
        });

        Assert.Equal(0x1122334455667788L, decoded.TimestampTicksUtc);
    }

    [Fact]
    public void PeekFrameType_ReturnsCorrectType()
    {
        using var req = RpcEnvelopeCodec.EncodeRequest(new RpcRequestEnvelope
            { RequestId = 1, ServiceId = 1, MethodId = 1 });
        using var resp = RpcEnvelopeCodec.EncodeResponse(new RpcResponseEnvelope
            { RequestId = 1, Status = RpcStatus.Ok });
        using var push = RpcEnvelopeCodec.EncodePush(new RpcPushEnvelope
            { ServiceId = 1, MethodId = 1 });
        using var ping = RpcEnvelopeCodec.EncodeKeepAlivePing(new RpcKeepAlivePingEnvelope
            { TimestampTicksUtc = 1 });
        using var pong = RpcEnvelopeCodec.EncodeKeepAlivePong(new RpcKeepAlivePongEnvelope
            { TimestampTicksUtc = 2 });

        Assert.Equal(RpcFrameType.Request, RpcEnvelopeCodec.PeekFrameType(req.Span));
        Assert.Equal(RpcFrameType.Response, RpcEnvelopeCodec.PeekFrameType(resp.Span));
        Assert.Equal(RpcFrameType.Push, RpcEnvelopeCodec.PeekFrameType(push.Span));
        Assert.Equal(RpcFrameType.KeepAlivePing, RpcEnvelopeCodec.PeekFrameType(ping.Span));
        Assert.Equal(RpcFrameType.KeepAlivePong, RpcEnvelopeCodec.PeekFrameType(pong.Span));
    }

    [Fact]
    public void PeekFrameType_EmptyData_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            RpcEnvelopeCodec.PeekFrameType(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void DecodeRequest_WrongFrameType_Throws()
    {
        using var push = RpcEnvelopeCodec.EncodePush(new RpcPushEnvelope
            { ServiceId = 1, MethodId = 1 });

        Assert.Throws<InvalidOperationException>(() =>
        {
            using var _ = RpcEnvelopeCodec.DecodeRequest(push);
        });
    }

    [Fact]
    public void EncodePush_NullArg_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RpcEnvelopeCodec.EncodePush(null!));
    }

    [Fact]
    public void RequestRoundTrip_EmptyPayload()
    {
        var original = new RpcRequestEnvelope
        {
            RequestId = 0,
            ServiceId = 0,
            MethodId = 0,
            Payload = Array.Empty<byte>()
        };

        using var encoded = RpcEnvelopeCodec.EncodeRequest(original);
        using var decoded = RpcEnvelopeCodec.DecodeRequest(encoded);

        Assert.True(decoded.Payload.IsEmpty);
    }

    [Fact]
    public void RequestRoundTrip_NullPayloadTreatedAsEmpty()
    {
        var original = new RpcRequestEnvelope
        {
            RequestId = 1,
            ServiceId = 1,
            MethodId = 1,
            Payload = null!
        };

        using var encoded = RpcEnvelopeCodec.EncodeRequest(original);
        using var decoded = RpcEnvelopeCodec.DecodeRequest(encoded);

        Assert.True(decoded.Payload.IsEmpty);
    }

    [Fact]
    public void ResponseRoundTrip_UnicodeErrorMessage()
    {
        var original = new RpcResponseEnvelope
        {
            RequestId = 5,
            Status = RpcStatus.HandlerError,
            Payload = Array.Empty<byte>(),
            ErrorMessage = "错误消息 with emoji 🎉"
        };

        using var encoded = RpcEnvelopeCodec.EncodeResponse(original);
        using var decoded = RpcEnvelopeCodec.DecodeResponse(encoded);

        Assert.Equal(original.ErrorMessage, decoded.ErrorMessage);
    }

    [Fact]
    public void MaxPayloadSize_Is64MB()
    {
        Assert.Equal(RpcProtocolLimits.DefaultMaxPayloadSize, RpcEnvelopeCodec.MaxPayloadSize);
    }
}
