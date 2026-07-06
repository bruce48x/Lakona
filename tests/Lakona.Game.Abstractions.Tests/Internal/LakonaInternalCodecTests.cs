using System.Buffers.Binary;
using System.Text;
using Lakona.Game.Abstractions;
using Lakona.Game.Abstractions.Sessions;
using Xunit;

namespace Lakona.Game.Abstractions.Tests.Internal;

public sealed class LakonaInternalCodecTests
{
    private const int Magic = 0x4C4B4943;
    private const byte CodecVersion = 1;
    private const byte GameClientHelloKind = 1;
    private const byte GameServerHelloKind = 2;
    private const byte GameHeartbeatRequestKind = 3;
    private const byte GameHeartbeatReplyKind = 4;
    private const byte ReliablePushAckRequestKind = 5;
    private const byte SessionTerminationNoticeKind = 7;

    [Fact]
    public void GameClientHello_roundtrips_protocol_version_only()
    {
        var hello = new GameClientHello { ProtocolVersion = 1 };

        var payload = LakonaInternalCodec.EncodeGameClientHello(hello);
        var decoded = LakonaInternalCodec.DecodeGameClientHello(payload);

        Assert.Equal(hello.ProtocolVersion, decoded.ProtocolVersion);
        Assert.Equal(10, payload.Length);
    }

    [Fact]
    public void GameServerHello_roundtrips_with_reliable_push_settings()
    {
        var hello = new GameServerHello
        {
            SelectedProtocolVersion = 2,
            ServerNodeId = "node-a",
            EndpointTransport = "tcp",
            EndpointSerializer = "lakona-internal",
            ReliablePush = new ReliablePushHandshakeSettings
            {
                Enabled = true,
                DeliveryMode = "reliable",
                AckRequired = true,
                ReplaySupported = true,
                MaxPending = 256,
            },
            ServerTimeUtc = new DateTimeOffset(2026, 6, 24, 10, 30, 0, TimeSpan.Zero),
        };

        var decoded = LakonaInternalCodec.DecodeGameServerHello(
            LakonaInternalCodec.EncodeGameServerHello(hello));

        Assert.Equal(hello.SelectedProtocolVersion, decoded.SelectedProtocolVersion);
        Assert.Equal(hello.ServerNodeId, decoded.ServerNodeId);
        Assert.Equal(hello.EndpointTransport, decoded.EndpointTransport);
        Assert.Equal(hello.EndpointSerializer, decoded.EndpointSerializer);
        Assert.Equal(hello.ReliablePush.Enabled, decoded.ReliablePush.Enabled);
        Assert.Equal(hello.ReliablePush.DeliveryMode, decoded.ReliablePush.DeliveryMode);
        Assert.Equal(hello.ReliablePush.AckRequired, decoded.ReliablePush.AckRequired);
        Assert.Equal(hello.ReliablePush.ReplaySupported, decoded.ReliablePush.ReplaySupported);
        Assert.Equal(hello.ReliablePush.MaxPending, decoded.ReliablePush.MaxPending);
        Assert.Equal(hello.ServerTimeUtc, decoded.ServerTimeUtc);
    }

    [Fact]
    public void GameHeartbeatRequest_roundtrips_protocol_version()
    {
        var request = new GameHeartbeatRequest { ProtocolVersion = 1 };

        var payload = LakonaInternalCodec.EncodeGameHeartbeatRequest(request);
        var decoded = LakonaInternalCodec.DecodeGameHeartbeatRequest(payload);

        Assert.Equal(request.ProtocolVersion, decoded.ProtocolVersion);
        Assert.Null(decoded.SessionId);
        Assert.Equal(0, decoded.SessionGeneration);
        Assert.Equal(10, payload.Length);
    }

    [Fact]
    public void GameHeartbeatRequest_roundtrips_session_identity()
    {
        var request = new GameHeartbeatRequest
        {
            ProtocolVersion = 1,
            SessionId = "session-a",
            SessionGeneration = 7
        };

        var decoded = LakonaInternalCodec.DecodeGameHeartbeatRequest(
            LakonaInternalCodec.EncodeGameHeartbeatRequest(request));

        Assert.Equal(request.ProtocolVersion, decoded.ProtocolVersion);
        Assert.Equal("session-a", decoded.SessionId);
        Assert.Equal(7, decoded.SessionGeneration);
    }

    [Theory]
    [InlineData(GameHeartbeatStatus.Ok)]
    [InlineData(GameHeartbeatStatus.StateLost)]
    [InlineData(GameHeartbeatStatus.Terminated)]
    public void GameHeartbeatReply_roundtrips_every_status(GameHeartbeatStatus status)
    {
        var reply = new GameHeartbeatReply
        {
            Status = status,
            Message = status == GameHeartbeatStatus.Ok ? null : "session state changed",
        };

        var decoded = LakonaInternalCodec.DecodeGameHeartbeatReply(
            LakonaInternalCodec.EncodeGameHeartbeatReply(reply));

        Assert.Equal(reply.Status, decoded.Status);
        Assert.Equal(reply.Message, decoded.Message);
    }

    [Fact]
    public void ReliablePushAckRequest_roundtrips_session_generation_and_sequence()
    {
        var request = new ReliablePushAckRequest("session-123", 7, ReliablePushSequence.From(42));

        var decoded = LakonaInternalCodec.DecodeReliablePushAckRequest(
            LakonaInternalCodec.EncodeReliablePushAckRequest(request));

        Assert.Equal(request.SessionId, decoded.SessionId);
        Assert.Equal(7, decoded.SessionGeneration);
        Assert.Equal(42, decoded.Sequence.Value);
    }

    [Fact]
    public void ReliablePushMetadata_roundtrips_all_fields()
    {
        var metadata = new ReliablePushMetadata(
            "session-123",
            7,
            ReliablePushSequence.From(42),
            "matchmaking.status");

        var decoded = LakonaInternalCodec.DecodeReliablePushMetadata(
            LakonaInternalCodec.EncodeReliablePushMetadata(metadata));

        Assert.Equal(metadata.SessionId, decoded.SessionId);
        Assert.Equal(metadata.SessionGeneration, decoded.SessionGeneration);
        Assert.Equal(metadata.Sequence.Value, decoded.Sequence.Value);
        Assert.Equal(metadata.Kind, decoded.Kind);
    }

    [Theory]
    [InlineData(ReliablePushAckStatus.Accepted)]
    [InlineData(ReliablePushAckStatus.Duplicate)]
    [InlineData(ReliablePushAckStatus.StateRefreshRequired)]
    [InlineData(ReliablePushAckStatus.StateLost)]
    [InlineData(ReliablePushAckStatus.SessionMismatch)]
    public void ReliablePushAckOutcome_roundtrips_every_status(ReliablePushAckStatus status)
    {
        var outcome = new ReliablePushAckOutcome(status, 42, status == ReliablePushAckStatus.Accepted ? null : "ack rejected");

        var decoded = LakonaInternalCodec.DecodeReliablePushAckOutcome(
            LakonaInternalCodec.EncodeReliablePushAckOutcome(outcome));

        Assert.Equal(outcome.Status, decoded.Status);
        Assert.Equal(outcome.Sequence, decoded.Sequence);
        Assert.Equal(outcome.Reason, decoded.Reason);
    }

    [Theory]
    [InlineData(SessionTerminationReason.ReplacedByNewLogin)]
    [InlineData(SessionTerminationReason.ServerShutdown)]
    [InlineData(SessionTerminationReason.Maintenance)]
    [InlineData(SessionTerminationReason.Unauthorized)]
    [InlineData(SessionTerminationReason.Policy)]
    [InlineData(SessionTerminationReason.StateLost)]
    [InlineData(SessionTerminationReason.Application)]
    public void SessionTerminationNotice_roundtrips_defined_reasons(SessionTerminationReason reason)
    {
        var notice = new SessionTerminationNotice(
            reason,
            "closed",
            new DateTimeOffset(2026, 6, 24, 11, 0, 0, TimeSpan.Zero));

        var decoded = LakonaInternalCodec.DecodeSessionTerminationNotice(
            LakonaInternalCodec.EncodeSessionTerminationNotice(notice));

        Assert.Equal(notice.Reason, decoded.Reason);
        Assert.Equal(notice.Message, decoded.Message);
        Assert.Equal(notice.IssuedAt, decoded.IssuedAt);
    }

    [Fact]
    public void Decode_rejects_wrong_magic()
    {
        var payload = LakonaInternalCodec.EncodeGameHeartbeatReply(new GameHeartbeatReply());
        WriteInt32BigEndian(payload, 0, 0x584B4943);

        Assert.Throws<InvalidOperationException>(() => LakonaInternalCodec.DecodeGameHeartbeatReply(payload));
    }

    [Fact]
    public void Decode_rejects_wrong_message_kind()
    {
        var payload = LakonaInternalCodec.EncodeGameHeartbeatReply(new GameHeartbeatReply());
        payload[5] = GameClientHelloKind;

        Assert.Throws<InvalidOperationException>(() => LakonaInternalCodec.DecodeGameHeartbeatReply(payload));
    }

    [Fact]
    public void Decode_rejects_trailing_bytes()
    {
        var payload = LakonaInternalCodec.EncodeGameHeartbeatReply(new GameHeartbeatReply());
        var withTrailingByte = payload.Concat(new byte[] { 0x7F }).ToArray();

        Assert.Throws<InvalidOperationException>(() => LakonaInternalCodec.DecodeGameHeartbeatReply(withTrailingByte));
    }

    [Fact]
    public void ReliablePushAckOutcome_allows_zero_sequence()
    {
        var outcome = ReliablePushAckOutcome.StateRefreshRequired("resync");

        var decoded = LakonaInternalCodec.DecodeReliablePushAckOutcome(
            LakonaInternalCodec.EncodeReliablePushAckOutcome(outcome));

        Assert.Equal(ReliablePushAckStatus.StateRefreshRequired, decoded.Status);
        Assert.Equal(0, decoded.Sequence);
        Assert.Equal("resync", decoded.Reason);
    }

    [Fact]
    public void Decode_rejects_unsupported_codec_version()
    {
        var payload = LakonaInternalCodec.EncodeGameHeartbeatReply(new GameHeartbeatReply());
        payload[4] = (byte)(CodecVersion + 1);

        Assert.Throws<InvalidOperationException>(() => LakonaInternalCodec.DecodeGameHeartbeatReply(payload));
    }

    [Fact]
    public void Decode_rejects_truncated_payload()
    {
        var payload = LakonaInternalCodec.EncodeGameHeartbeatReply(
            new GameHeartbeatReply { Status = GameHeartbeatStatus.Terminated, Message = "closed" });
        var truncated = payload[..^1];

        Assert.Throws<InvalidOperationException>(() => LakonaInternalCodec.DecodeGameHeartbeatReply(truncated));
    }

    [Fact]
    public void Decode_rejects_negative_string_length_other_than_null_marker()
    {
        var payload = CreatePayload(GameHeartbeatReplyKind, builder =>
        {
            WriteInt32BigEndian(builder, (int)GameHeartbeatStatus.Terminated);
            WriteInt32BigEndian(builder, -2);
        });

        Assert.Throws<InvalidOperationException>(() => LakonaInternalCodec.DecodeGameHeartbeatReply(payload));
    }

    [Fact]
    public void Decode_rejects_oversized_string_length()
    {
        var payload = CreatePayload(GameHeartbeatReplyKind, builder =>
        {
            WriteInt32BigEndian(builder, (int)GameHeartbeatStatus.Terminated);
            WriteInt32BigEndian(builder, 1024 * 1024 + 1);
        });

        Assert.Throws<InvalidOperationException>(() => LakonaInternalCodec.DecodeGameHeartbeatReply(payload));
    }

    [Fact]
    public void Decode_rejects_malformed_utf8_string_payload()
    {
        var payload = CreatePayload(GameHeartbeatReplyKind, builder =>
        {
            WriteInt32BigEndian(builder, (int)GameHeartbeatStatus.Terminated);
            WriteInt32BigEndian(builder, 1);
            builder.Add(0xFF);
        });

        Assert.Throws<InvalidOperationException>(() => LakonaInternalCodec.DecodeGameHeartbeatReply(payload));
    }

    [Fact]
    public void Decode_rejects_invalid_game_client_hello_protocol_version()
    {
        var payload = CreatePayload(GameClientHelloKind, builder => WriteInt32BigEndian(builder, 0));

        Assert.Throws<InvalidOperationException>(() => LakonaInternalCodec.DecodeGameClientHello(payload));
    }

    [Fact]
    public void Decode_rejects_invalid_bool_byte()
    {
        var payload = CreatePayload(GameServerHelloKind, builder =>
        {
            WriteInt32BigEndian(builder, 1);
            WriteString(builder, "node-a");
            WriteString(builder, "tcp");
            WriteString(builder, "lakona-internal");
            builder.Add(2);
            WriteString(builder, "reliable");
            builder.Add(1);
            builder.Add(1);
            WriteInt32BigEndian(builder, 128);
            WriteInt64BigEndian(builder, 1_782_300_600_000);
            WriteInt32BigEndian(builder, 0);
        });

        Assert.Throws<InvalidOperationException>(() => LakonaInternalCodec.DecodeGameServerHello(payload));
    }

    [Fact]
    public void Decode_rejects_negative_game_server_hello_reliable_push_max_pending()
    {
        var payload = CreatePayload(GameServerHelloKind, builder =>
        {
            WriteInt32BigEndian(builder, 1);
            WriteString(builder, "node-a");
            WriteString(builder, "tcp");
            WriteString(builder, "lakona-internal");
            builder.Add(1);
            WriteString(builder, "reliable");
            builder.Add(1);
            builder.Add(1);
            WriteInt32BigEndian(builder, -1);
            WriteInt64BigEndian(builder, new DateTimeOffset(2026, 6, 24, 10, 30, 0, TimeSpan.Zero).Ticks);
            WriteInt16BigEndian(builder, 0);
            WriteInt32BigEndian(builder, 0);
        });

        Assert.Throws<InvalidOperationException>(() => LakonaInternalCodec.DecodeGameServerHello(payload));
    }

    [Fact]
    public void Decode_rejects_out_of_range_datetime_ticks_with_invalid_operation_exception()
    {
        var payload = CreatePayload(SessionTerminationNoticeKind, builder =>
        {
            WriteInt32BigEndian(builder, (int)SessionTerminationReason.ServerShutdown);
            WriteString(builder, "closed");
            WriteInt64BigEndian(builder, long.MaxValue);
            WriteInt16BigEndian(builder, 0);
        });

        Assert.Throws<InvalidOperationException>(() => LakonaInternalCodec.DecodeSessionTerminationNotice(payload));
    }

    [Fact]
    public void Decode_rejects_invalid_enum_value()
    {
        var payload = CreatePayload(GameHeartbeatReplyKind, builder =>
        {
            WriteInt32BigEndian(builder, 999);
            WriteInt32BigEndian(builder, -1);
        });

        Assert.Throws<InvalidOperationException>(() => LakonaInternalCodec.DecodeGameHeartbeatReply(payload));
    }

    [Fact]
    public void Decode_rejects_invalid_protocol_version()
    {
        var payload = CreatePayload(GameHeartbeatRequestKind, builder => WriteInt32BigEndian(builder, 0));

        Assert.Throws<InvalidOperationException>(() => LakonaInternalCodec.DecodeGameHeartbeatRequest(payload));
    }

    [Fact]
    public void Decode_rejects_heartbeat_session_identity_without_positive_generation()
    {
        var payload = CreatePayload(GameHeartbeatRequestKind, builder =>
        {
            WriteInt32BigEndian(builder, 1);
            WriteString(builder, "session-a");
            WriteInt64BigEndian(builder, 0);
        });

        Assert.Throws<InvalidOperationException>(() => LakonaInternalCodec.DecodeGameHeartbeatRequest(payload));
    }

    [Fact]
    public void Encode_rejects_heartbeat_generation_without_session_identity()
    {
        var request = new GameHeartbeatRequest
        {
            ProtocolVersion = 1,
            SessionGeneration = 1
        };

        Assert.Throws<InvalidOperationException>(() => LakonaInternalCodec.EncodeGameHeartbeatRequest(request));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Decode_rejects_invalid_reliable_push_ack_request_sequence(long sequence)
    {
        var payload = CreatePayload(ReliablePushAckRequestKind, builder =>
        {
            WriteString(builder, "session-123");
            WriteInt64BigEndian(builder, 1);
            WriteInt64BigEndian(builder, sequence);
        });

        Assert.Throws<InvalidOperationException>(() => LakonaInternalCodec.DecodeReliablePushAckRequest(payload));
    }

    [Fact]
    public void Decode_rejects_invalid_reliable_push_ack_request_generation()
    {
        var payload = CreatePayload(ReliablePushAckRequestKind, builder =>
        {
            WriteString(builder, "session-123");
            WriteInt64BigEndian(builder, 0);
            WriteInt64BigEndian(builder, 1);
        });

        Assert.Throws<InvalidOperationException>(() => LakonaInternalCodec.DecodeReliablePushAckRequest(payload));
    }

    private static byte[] CreatePayload(byte messageKind, Action<List<byte>> writePayload)
    {
        var payload = new List<byte>();
        WriteInt32BigEndian(payload, Magic);
        payload.Add(CodecVersion);
        payload.Add(messageKind);
        writePayload(payload);
        return payload.ToArray();
    }

    private static void WriteString(List<byte> payload, string? value)
    {
        if (value is null)
        {
            WriteInt32BigEndian(payload, -1);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        WriteInt32BigEndian(payload, bytes.Length);
        payload.AddRange(bytes);
    }

    private static void WriteInt32BigEndian(List<byte> payload, int value)
    {
        var bytes = new byte[sizeof(int)];
        WriteInt32BigEndian(bytes, 0, value);
        payload.AddRange(bytes);
    }

    private static void WriteInt64BigEndian(List<byte> payload, long value)
    {
        var bytes = new byte[sizeof(long)];
        WriteInt64BigEndian(bytes, 0, value);
        payload.AddRange(bytes);
    }

    private static void WriteInt16BigEndian(List<byte> payload, short value)
    {
        var bytes = new byte[sizeof(short)];
        BinaryPrimitives.WriteInt16BigEndian(bytes, value);
        payload.AddRange(bytes);
    }

    private static void WriteInt32BigEndian(byte[] bytes, int offset, int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset, sizeof(int)), value);
    }

    private static void WriteInt64BigEndian(byte[] bytes, int offset, long value)
    {
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(offset, sizeof(long)), value);
    }
}
