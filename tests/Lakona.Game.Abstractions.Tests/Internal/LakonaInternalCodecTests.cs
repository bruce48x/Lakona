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
    private const byte GameSessionEstablishedKind = 9;

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
    public void GameClientHello_roundtrips_an_opaque_resume_ticket()
    {
        var hello = new GameClientHello
        {
            ProtocolVersion = 1,
            ResumeTicket = "opaque-ticket-a"
        };

        var decoded = LakonaInternalCodec.DecodeGameClientHello(
            LakonaInternalCodec.EncodeGameClientHello(hello));

        Assert.Equal("opaque-ticket-a", decoded.ResumeTicket);
    }

    [Fact]
    public void GameServerHello_roundtrips_with_runtime_policies_only()
    {
        var hello = new GameServerHello
        {
            SelectedProtocolVersion = 1,
            SessionResume = new GameSessionResumeHandshakeSettings
            {
                Window = TimeSpan.FromSeconds(60),
            },
            ReliablePush = new ReliablePushHandshakeSettings
            {
                Enabled = true,
                AckRequired = true,
            },
            Heartbeat = new GameHeartbeatHandshakeSettings
            {
                Interval = TimeSpan.FromSeconds(7),
                Timeout = TimeSpan.FromSeconds(21),
            },
        };

        var payload = LakonaInternalCodec.EncodeGameServerHello(hello);
        var decoded = LakonaInternalCodec.DecodeGameServerHello(payload);

        Assert.Equal(hello.SelectedProtocolVersion, decoded.SelectedProtocolVersion);
        Assert.Equal(hello.ReliablePush.Enabled, decoded.ReliablePush.Enabled);
        Assert.Equal(hello.ReliablePush.AckRequired, decoded.ReliablePush.AckRequired);
        Assert.Equal(TimeSpan.FromSeconds(60), decoded.SessionResume.Window);
        Assert.Equal(TimeSpan.FromSeconds(7), decoded.Heartbeat.Interval);
        Assert.Equal(TimeSpan.FromSeconds(21), decoded.Heartbeat.Timeout);
        Assert.Equal(GameSessionRecoveryStatus.NotRequested, decoded.Recovery.Status);
    }

    [Fact]
    public void GameServerHello_roundtrips_recovery_outcome()
    {
        var hello = new GameServerHello
        {
            SelectedProtocolVersion = 1,
            Recovery = new GameSessionRecoveryHandshakeResult
            {
                Status = GameSessionRecoveryStatus.Resumed,
                Reason = "restored"
            }
        };

        var decoded = LakonaInternalCodec.DecodeGameServerHello(
            LakonaInternalCodec.EncodeGameServerHello(hello));

        Assert.Equal(GameSessionRecoveryStatus.Resumed, decoded.Recovery.Status);
        Assert.Equal("restored", decoded.Recovery.Reason);
    }

    [Fact]
    public void GameSessionEstablished_roundtrips_framework_identity_and_ticket()
    {
        var established = new GameSessionEstablished
        {
            SessionId = "session-a",
            ResumeTicket = "opaque-ticket-a"
        };

        var payload = LakonaInternalCodec.EncodeGameSessionEstablished(established);
        var decoded = LakonaInternalCodec.DecodeGameSessionEstablished(payload);

        Assert.Equal("session-a", decoded.SessionId);
        Assert.Equal("opaque-ticket-a", decoded.ResumeTicket);
        Assert.Equal(GameSessionEstablishedKind, payload[5]);
    }

    [Fact]
    public void GameServerHello_contract_excludes_connection_facts_and_server_internals()
    {
        var serverHelloProperties = typeof(GameServerHello)
            .GetProperties()
            .Select(static property => property.Name)
            .ToArray();
        var reliablePushProperties = typeof(ReliablePushHandshakeSettings)
            .GetProperties()
            .Select(static property => property.Name)
            .ToArray();

        Assert.DoesNotContain("ServerNodeId", serverHelloProperties);
        Assert.DoesNotContain("EndpointTransport", serverHelloProperties);
        Assert.DoesNotContain("EndpointSerializer", serverHelloProperties);
        Assert.DoesNotContain("ServerTimeUtc", serverHelloProperties);
        Assert.DoesNotContain("DeliveryMode", reliablePushProperties);
        Assert.DoesNotContain("ReplaySupported", reliablePushProperties);
        Assert.DoesNotContain("MaxPending", reliablePushProperties);
    }

    [Theory]
    [InlineData(0, 450000000)]
    [InlineData(-1, 450000000)]
    [InlineData(150000000, 0)]
    [InlineData(150000000, -1)]
    [InlineData(450000000, 150000000)]
    public void GameServerHello_rejects_invalid_heartbeat_settings(long intervalTicks, long timeoutTicks)
    {
        var hello = new GameServerHello
        {
            SelectedProtocolVersion = 1,
            Heartbeat = new GameHeartbeatHandshakeSettings
            {
                Interval = TimeSpan.FromTicks(intervalTicks),
                Timeout = TimeSpan.FromTicks(timeoutTicks),
            },
        };

        Assert.Throws<InvalidOperationException>(() => LakonaInternalCodec.EncodeGameServerHello(hello));
    }

    [Fact]
    public void GameHeartbeatRequest_roundtrips_protocol_version()
    {
        var request = new GameHeartbeatRequest { ProtocolVersion = 1 };

        var payload = LakonaInternalCodec.EncodeGameHeartbeatRequest(request);
        var decoded = LakonaInternalCodec.DecodeGameHeartbeatRequest(payload);

        Assert.Equal(request.ProtocolVersion, decoded.ProtocolVersion);
        Assert.Null(decoded.SessionId);
        Assert.Equal(10, payload.Length);
    }

    [Fact]
    public void GameHeartbeatRequest_roundtrips_session_identity()
    {
        var request = new GameHeartbeatRequest
        {
            ProtocolVersion = 1,
            SessionId = "session-a"
        };

        var decoded = LakonaInternalCodec.DecodeGameHeartbeatRequest(
            LakonaInternalCodec.EncodeGameHeartbeatRequest(request));

        Assert.Equal(request.ProtocolVersion, decoded.ProtocolVersion);
        Assert.Equal("session-a", decoded.SessionId);
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
    public void ReliablePushAckRequest_roundtrips_session_and_sequence()
    {
        var request = new ReliablePushAckRequest("session-123", ReliablePushSequence.From(42));

        var decoded = LakonaInternalCodec.DecodeReliablePushAckRequest(
            LakonaInternalCodec.EncodeReliablePushAckRequest(request));

        Assert.Equal(request.SessionId, decoded.SessionId);
        Assert.Equal(42, decoded.Sequence.Value);
    }

    [Fact]
    public void ReliablePushMetadata_roundtrips_all_fields()
    {
        var metadata = new ReliablePushMetadata(
            "session-123",
            ReliablePushSequence.From(42),
            "matchmaking.status");

        var decoded = LakonaInternalCodec.DecodeReliablePushMetadata(
            LakonaInternalCodec.EncodeReliablePushMetadata(metadata));

        Assert.Equal(metadata.SessionId, decoded.SessionId);
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
            builder.Add(2);
            builder.Add(1);
            WriteInt64BigEndian(builder, TimeSpan.FromSeconds(15).Ticks);
            WriteInt64BigEndian(builder, TimeSpan.FromSeconds(45).Ticks);
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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Decode_rejects_invalid_reliable_push_ack_request_sequence(long sequence)
    {
        var payload = CreatePayload(ReliablePushAckRequestKind, builder =>
        {
            WriteString(builder, "session-123");
            WriteInt64BigEndian(builder, sequence);
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
