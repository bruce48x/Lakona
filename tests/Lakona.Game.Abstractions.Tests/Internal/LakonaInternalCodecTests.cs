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

    [Fact]
    public void GameClientHello_roundtrips_with_all_fields()
    {
        var hello = new GameClientHello
        {
            ProtocolVersionMin = 1,
            ProtocolVersionMax = 3,
            ClientRuntime = "unity",
            ClientRuntimeVersion = "2022.3.59f1",
            GameVersion = "1.2.3",
            BuildId = "build-456",
            Platform = "Windows",
            SupportedCapabilities = new List<string> { "resume", "reliable-push" },
        };

        var decoded = LakonaInternalCodec.DecodeGameClientHello(
            LakonaInternalCodec.EncodeGameClientHello(hello));

        Assert.Equal(hello.ProtocolVersionMin, decoded.ProtocolVersionMin);
        Assert.Equal(hello.ProtocolVersionMax, decoded.ProtocolVersionMax);
        Assert.Equal(hello.ClientRuntime, decoded.ClientRuntime);
        Assert.Equal(hello.ClientRuntimeVersion, decoded.ClientRuntimeVersion);
        Assert.Equal(hello.GameVersion, decoded.GameVersion);
        Assert.Equal(hello.BuildId, decoded.BuildId);
        Assert.Equal(hello.Platform, decoded.Platform);
        Assert.Equal(hello.SupportedCapabilities, decoded.SupportedCapabilities);
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
            ServerCapabilities = new List<string> { "heartbeat", "replay" },
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
        Assert.Equal(hello.ServerCapabilities, decoded.ServerCapabilities);
    }

    [Fact]
    public void GameHeartbeatRequest_roundtrips_protocol_version()
    {
        var request = new GameHeartbeatRequest { ProtocolVersion = 1 };

        var decoded = LakonaInternalCodec.DecodeGameHeartbeatRequest(
            LakonaInternalCodec.EncodeGameHeartbeatRequest(request));

        Assert.Equal(request.ProtocolVersion, decoded.ProtocolVersion);
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
    public void ReliablePushAckRequest_roundtrips_sequence()
    {
        var request = new ReliablePushAckRequest("session-123", 42);

        var decoded = LakonaInternalCodec.DecodeReliablePushAckRequest(
            LakonaInternalCodec.EncodeReliablePushAckRequest(request));

        Assert.Equal(request.SessionId, decoded.SessionId);
        Assert.Equal(request.Sequence, decoded.Sequence);
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

        Assert.Throws<FormatException>(() => LakonaInternalCodec.DecodeGameHeartbeatReply(payload));
    }

    [Fact]
    public void Decode_rejects_wrong_message_kind()
    {
        var payload = LakonaInternalCodec.EncodeGameHeartbeatReply(new GameHeartbeatReply());
        payload[5] = GameClientHelloKind;

        Assert.Throws<FormatException>(() => LakonaInternalCodec.DecodeGameHeartbeatReply(payload));
    }

    [Fact]
    public void Decode_rejects_trailing_bytes()
    {
        var payload = LakonaInternalCodec.EncodeGameHeartbeatReply(new GameHeartbeatReply());
        var withTrailingByte = payload.Concat(new byte[] { 0x7F }).ToArray();

        Assert.Throws<FormatException>(() => LakonaInternalCodec.DecodeGameHeartbeatReply(withTrailingByte));
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

        Assert.Throws<FormatException>(() => LakonaInternalCodec.DecodeGameHeartbeatReply(payload));
    }

    [Fact]
    public void Decode_rejects_truncated_payload()
    {
        var payload = LakonaInternalCodec.EncodeGameHeartbeatReply(
            new GameHeartbeatReply { Status = GameHeartbeatStatus.Terminated, Message = "closed" });
        var truncated = payload[..^1];

        Assert.Throws<FormatException>(() => LakonaInternalCodec.DecodeGameHeartbeatReply(truncated));
    }

    [Fact]
    public void Decode_rejects_negative_string_length_other_than_null_marker()
    {
        var payload = CreatePayload(GameHeartbeatReplyKind, builder =>
        {
            WriteInt32BigEndian(builder, (int)GameHeartbeatStatus.Terminated);
            WriteInt32BigEndian(builder, -2);
        });

        Assert.Throws<FormatException>(() => LakonaInternalCodec.DecodeGameHeartbeatReply(payload));
    }

    [Fact]
    public void Decode_rejects_oversized_string_length()
    {
        var payload = CreatePayload(GameHeartbeatReplyKind, builder =>
        {
            WriteInt32BigEndian(builder, (int)GameHeartbeatStatus.Terminated);
            WriteInt32BigEndian(builder, 1024 * 1024 + 1);
        });

        Assert.Throws<FormatException>(() => LakonaInternalCodec.DecodeGameHeartbeatReply(payload));
    }

    [Fact]
    public void Decode_rejects_oversized_string_list_count()
    {
        var payload = CreatePayload(GameClientHelloKind, builder =>
        {
            WriteInt32BigEndian(builder, 1);
            WriteInt32BigEndian(builder, 1);
            WriteString(builder, "unity");
            WriteString(builder, "2022.3");
            WriteString(builder, "1.0.0");
            WriteString(builder, "build-1");
            WriteString(builder, "Windows");
            WriteInt32BigEndian(builder, 1024 * 1024 + 1);
        });

        Assert.Throws<FormatException>(() => LakonaInternalCodec.DecodeGameClientHello(payload));
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

        Assert.Throws<FormatException>(() => LakonaInternalCodec.DecodeGameServerHello(payload));
    }

    [Fact]
    public void Decode_rejects_invalid_enum_value()
    {
        var payload = CreatePayload(GameHeartbeatReplyKind, builder =>
        {
            WriteInt32BigEndian(builder, 999);
            WriteInt32BigEndian(builder, -1);
        });

        Assert.Throws<FormatException>(() => LakonaInternalCodec.DecodeGameHeartbeatReply(payload));
    }

    [Fact]
    public void Decode_rejects_invalid_protocol_version()
    {
        var payload = CreatePayload(GameHeartbeatRequestKind, builder => WriteInt32BigEndian(builder, 0));

        Assert.Throws<FormatException>(() => LakonaInternalCodec.DecodeGameHeartbeatRequest(payload));
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

        Assert.Throws<FormatException>(() => LakonaInternalCodec.DecodeReliablePushAckRequest(payload));
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

    private static void WriteInt32BigEndian(byte[] bytes, int offset, int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset, sizeof(int)), value);
    }

    private static void WriteInt64BigEndian(byte[] bytes, int offset, long value)
    {
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(offset, sizeof(long)), value);
    }
}
