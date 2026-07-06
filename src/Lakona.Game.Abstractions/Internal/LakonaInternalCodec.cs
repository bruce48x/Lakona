using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Lakona.Game.Abstractions.Sessions;

namespace Lakona.Game.Abstractions
{
    public static class LakonaInternalCodec
    {
        public const string ReliablePushMetadataType = "lakona.game.reliable-push";

        public const int MaxPayloadSize = 64 * 1024;

        private const int Magic = 0x4C4B4943;
        private const byte Version = 1;
        private const int MaxStringListCount = 1024;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private const byte GameClientHelloKind = 1;
        private const byte GameServerHelloKind = 2;
        private const byte GameHeartbeatRequestKind = 3;
        private const byte GameHeartbeatReplyKind = 4;
        private const byte ReliablePushAckRequestKind = 5;
        private const byte ReliablePushAckOutcomeKind = 6;
        private const byte SessionTerminationNoticeKind = 7;
        private const byte ReliablePushMetadataKind = 8;

        public static byte[] EncodeGameClientHello(GameClientHello value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            ValidatePositiveProtocolVersion(value.ProtocolVersion);

            var writer = CreateWriter(GameClientHelloKind);
            writer.WriteInt32(value.ProtocolVersion);
            return writer.ToArray();
        }

        public static GameClientHello DecodeGameClientHello(ReadOnlyMemory<byte> payload)
        {
            var reader = CreateReader(payload, GameClientHelloKind);
            var value = new GameClientHello
            {
                ProtocolVersion = reader.ReadInt32(),
            };

            ValidatePositiveProtocolVersion(value.ProtocolVersion);
            reader.EnsureEnd();
            return value;
        }

        public static byte[] EncodeGameServerHello(GameServerHello value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            ValidatePositiveProtocolVersion(value.SelectedProtocolVersion);
            var reliablePush = value.ReliablePush ?? new ReliablePushHandshakeSettings();
            ValidateNonNegative(reliablePush.MaxPending, nameof(reliablePush.MaxPending));

            var writer = CreateWriter(GameServerHelloKind);
            writer.WriteInt32(value.SelectedProtocolVersion);
            writer.WriteString(value.ServerNodeId);
            writer.WriteString(value.EndpointTransport);
            writer.WriteString(value.EndpointSerializer);
            writer.WriteBool(reliablePush.Enabled);
            writer.WriteString(reliablePush.DeliveryMode);
            writer.WriteBool(reliablePush.AckRequired);
            writer.WriteBool(reliablePush.ReplaySupported);
            writer.WriteInt32(reliablePush.MaxPending);
            writer.WriteUtcDateTimeOffset(value.ServerTimeUtc);
            writer.WriteStringList(value.ServerCapabilities);
            return writer.ToArray();
        }

        public static GameServerHello DecodeGameServerHello(ReadOnlyMemory<byte> payload)
        {
            var reader = CreateReader(payload, GameServerHelloKind);
            var value = new GameServerHello
            {
                SelectedProtocolVersion = reader.ReadInt32(),
                ServerNodeId = reader.ReadStringAsEmptyIfNull(),
                EndpointTransport = reader.ReadStringAsEmptyIfNull(),
                EndpointSerializer = reader.ReadStringAsEmptyIfNull(),
                ReliablePush = new ReliablePushHandshakeSettings
                {
                    Enabled = reader.ReadBool(),
                    DeliveryMode = reader.ReadStringAsEmptyIfNull(),
                    AckRequired = reader.ReadBool(),
                    ReplaySupported = reader.ReadBool(),
                    MaxPending = reader.ReadInt32(),
                },
                ServerTimeUtc = reader.ReadUtcDateTimeOffset(),
                ServerCapabilities = reader.ReadStringListAsEmptyIfNull(),
            };

            ValidatePositiveProtocolVersion(value.SelectedProtocolVersion);
            ValidateNonNegative(value.ReliablePush.MaxPending, nameof(value.ReliablePush.MaxPending));
            reader.EnsureEnd();
            return value;
        }

        public static byte[] EncodeGameHeartbeatRequest(GameHeartbeatRequest value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            ValidatePositiveProtocolVersion(value.ProtocolVersion);

            var writer = CreateWriter(GameHeartbeatRequestKind);
            writer.WriteInt32(value.ProtocolVersion);
            if (!string.IsNullOrEmpty(value.SessionId))
            {
                ValidatePositiveSessionGeneration(value.SessionGeneration);
                writer.WriteString(value.SessionId);
                writer.WriteInt64(value.SessionGeneration);
            }
            else if (value.SessionGeneration != 0)
            {
                throw new InvalidOperationException(
                    "Heartbeat session generation requires a session id.");
            }

            return writer.ToArray();
        }

        public static GameHeartbeatRequest DecodeGameHeartbeatRequest(ReadOnlyMemory<byte> payload)
        {
            var reader = CreateReader(payload, GameHeartbeatRequestKind);
            var value = new GameHeartbeatRequest { ProtocolVersion = reader.ReadInt32() };
            if (reader.HasRemaining)
            {
                value.SessionId = reader.ReadString();
                value.SessionGeneration = reader.ReadInt64();
            }

            ValidatePositiveProtocolVersion(value.ProtocolVersion);
            if (!string.IsNullOrEmpty(value.SessionId))
            {
                ValidatePositiveSessionGeneration(value.SessionGeneration);
            }
            else
            {
                ValidateNonNegative(value.SessionGeneration, nameof(value.SessionGeneration));
            }

            reader.EnsureEnd();
            return value;
        }

        public static byte[] EncodeGameHeartbeatReply(GameHeartbeatReply value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            ValidateEnum(value.Status, nameof(value.Status));

            var writer = CreateWriter(GameHeartbeatReplyKind);
            writer.WriteInt32((int)value.Status);
            writer.WriteString(value.Message);
            return writer.ToArray();
        }

        public static GameHeartbeatReply DecodeGameHeartbeatReply(ReadOnlyMemory<byte> payload)
        {
            var reader = CreateReader(payload, GameHeartbeatReplyKind);
            var status = reader.ReadEnum<GameHeartbeatStatus>();
            var value = new GameHeartbeatReply
            {
                Status = status,
                Message = reader.ReadString(),
            };

            reader.EnsureEnd();
            return value;
        }

        public static byte[] EncodeReliablePushAckRequest(ReliablePushAckRequest value)
        {
            ValidateRequiredString(value.SessionId, nameof(value.SessionId));
            ValidatePositiveSessionGeneration(value.SessionGeneration);
            ValidatePositiveSequence(value.Sequence.Value);

            var writer = CreateWriter(ReliablePushAckRequestKind);
            writer.WriteString(value.SessionId);
            writer.WriteInt64(value.SessionGeneration);
            writer.WriteInt64(value.Sequence.Value);
            return writer.ToArray();
        }

        public static ReliablePushAckRequest DecodeReliablePushAckRequest(ReadOnlyMemory<byte> payload)
        {
            var reader = CreateReader(payload, ReliablePushAckRequestKind);
            var sessionId = reader.ReadString();
            var sessionGeneration = reader.ReadInt64();
            var sequence = reader.ReadInt64();
            ValidateRequiredString(sessionId, nameof(sessionId));
            ValidatePositiveSessionGeneration(sessionGeneration);
            ValidatePositiveSequence(sequence);
            reader.EnsureEnd();
            return new ReliablePushAckRequest(sessionId!, sessionGeneration, ReliablePushSequence.From(sequence));
        }

        public static byte[] EncodeReliablePushMetadata(ReliablePushMetadata value)
        {
            ValidateRequiredString(value.SessionId, nameof(value.SessionId));
            ValidatePositiveSessionGeneration(value.SessionGeneration);
            ValidatePositiveSequence(value.Sequence.Value);
            ValidateRequiredString(value.Kind, nameof(value.Kind));

            var writer = CreateWriter(ReliablePushMetadataKind);
            writer.WriteString(value.SessionId);
            writer.WriteInt64(value.SessionGeneration);
            writer.WriteInt64(value.Sequence.Value);
            writer.WriteString(value.Kind);
            return writer.ToArray();
        }

        public static ReliablePushMetadata DecodeReliablePushMetadata(ReadOnlyMemory<byte> payload)
        {
            var reader = CreateReader(payload, ReliablePushMetadataKind);
            var sessionId = reader.ReadString();
            var sessionGeneration = reader.ReadInt64();
            var sequence = reader.ReadInt64();
            var kind = reader.ReadString();
            ValidateRequiredString(sessionId, nameof(sessionId));
            ValidatePositiveSessionGeneration(sessionGeneration);
            ValidatePositiveSequence(sequence);
            ValidateRequiredString(kind, nameof(kind));
            reader.EnsureEnd();
            return new ReliablePushMetadata(
                sessionId!,
                sessionGeneration,
                ReliablePushSequence.From(sequence),
                kind!);
        }

        public static byte[] EncodeReliablePushAckOutcome(ReliablePushAckOutcome value)
        {
            ValidateEnum(value.Status, nameof(value.Status));
            ValidateNonNegative(value.Sequence, nameof(value.Sequence));

            var writer = CreateWriter(ReliablePushAckOutcomeKind);
            writer.WriteInt32((int)value.Status);
            writer.WriteInt64(value.Sequence);
            writer.WriteString(value.Reason);
            return writer.ToArray();
        }

        public static ReliablePushAckOutcome DecodeReliablePushAckOutcome(ReadOnlyMemory<byte> payload)
        {
            var reader = CreateReader(payload, ReliablePushAckOutcomeKind);
            var status = reader.ReadEnum<ReliablePushAckStatus>();
            var sequence = reader.ReadInt64();
            var reason = reader.ReadString();
            ValidateNonNegative(sequence, nameof(sequence));
            reader.EnsureEnd();
            return new ReliablePushAckOutcome(status, sequence, reason);
        }

        public static byte[] EncodeSessionTerminationNotice(SessionTerminationNotice value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            ValidateEnum(value.Reason, nameof(value.Reason));

            var writer = CreateWriter(SessionTerminationNoticeKind);
            writer.WriteInt32((int)value.Reason);
            writer.WriteString(value.Message);
            writer.WriteUtcDateTimeOffset(value.IssuedAt);
            return writer.ToArray();
        }

        public static SessionTerminationNotice DecodeSessionTerminationNotice(ReadOnlyMemory<byte> payload)
        {
            var reader = CreateReader(payload, SessionTerminationNoticeKind);
            var reason = reader.ReadEnum<SessionTerminationReason>();
            var message = reader.ReadString();
            var issuedAt = reader.ReadUtcDateTimeOffset();
            reader.EnsureEnd();
            return new SessionTerminationNotice(reason, message, issuedAt);
        }

        private static PayloadWriter CreateWriter(byte kind)
        {
            var writer = new PayloadWriter();
            writer.WriteInt32(Magic);
            writer.WriteByte(Version);
            writer.WriteByte(kind);
            return writer;
        }

        private static PayloadReader CreateReader(ReadOnlyMemory<byte> payload, byte expectedKind)
        {
            if (payload.Length > MaxPayloadSize)
            {
                throw new InvalidOperationException("Payload exceeds the maximum allowed size.");
            }

            var reader = new PayloadReader(payload);
            if (reader.ReadInt32() != Magic)
            {
                throw new InvalidOperationException("Payload has an invalid magic header.");
            }

            if (reader.ReadByte() != Version)
            {
                throw new InvalidOperationException("Payload has an unsupported codec version.");
            }

            if (reader.ReadByte() != expectedKind)
            {
                throw new InvalidOperationException("Payload message kind does not match the expected DTO.");
            }

            return reader;
        }

        private static void ValidatePositiveProtocolVersion(int value)
        {
            if (value <= 0)
            {
                throw new InvalidOperationException("Protocol version must be positive.");
            }
        }

        private static void ValidatePositiveSequence(long value)
        {
            if (value <= 0)
            {
                throw new InvalidOperationException("Reliable push ack request sequence must be positive.");
            }
        }

        private static void ValidatePositiveSessionGeneration(long value)
        {
            if (value <= 0)
            {
                throw new InvalidOperationException("Reliable push session generation must be positive.");
            }
        }

        private static void ValidateRequiredString(string? value, string name)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new InvalidOperationException(name + " cannot be null or empty.");
            }
        }

        private static void ValidateNonNegative(int value, string name)
        {
            if (value < 0)
            {
                throw new InvalidOperationException(name + " cannot be negative.");
            }
        }

        private static void ValidateNonNegative(long value, string name)
        {
            if (value < 0)
            {
                throw new InvalidOperationException(name + " cannot be negative.");
            }
        }

        private static void ValidateEnum<TEnum>(TEnum value, string name)
            where TEnum : struct, Enum
        {
            if (!Enum.IsDefined(typeof(TEnum), value))
            {
                throw new InvalidOperationException(name + " has an invalid value.");
            }
        }

        private sealed class PayloadWriter
        {
            private readonly List<byte> bytes = new List<byte>();

            public void WriteByte(byte value)
            {
                bytes.Add(value);
            }

            public void WriteBool(bool value)
            {
                bytes.Add(value ? (byte)1 : (byte)0);
            }

            public void WriteInt16(short value)
            {
                var buffer = new byte[sizeof(short)];
                BinaryPrimitives.WriteInt16BigEndian(buffer, value);
                bytes.AddRange(buffer);
            }

            public void WriteInt32(int value)
            {
                var buffer = new byte[sizeof(int)];
                BinaryPrimitives.WriteInt32BigEndian(buffer, value);
                bytes.AddRange(buffer);
            }

            public void WriteInt64(long value)
            {
                var buffer = new byte[sizeof(long)];
                BinaryPrimitives.WriteInt64BigEndian(buffer, value);
                bytes.AddRange(buffer);
            }

            public void WriteString(string? value)
            {
                if (value is null)
                {
                    WriteInt32(-1);
                    return;
                }

                var encoded = StrictUtf8.GetBytes(value);
                if (encoded.Length > MaxPayloadSize)
                {
                    throw new InvalidOperationException("String exceeds the maximum allowed size.");
                }

                WriteInt32(encoded.Length);
                bytes.AddRange(encoded);
            }

            public void WriteStringList(IReadOnlyCollection<string>? values)
            {
                if (values is null)
                {
                    WriteInt32(-1);
                    return;
                }

                if (values.Count > MaxStringListCount)
                {
                    throw new InvalidOperationException("String list exceeds the maximum allowed count.");
                }

                WriteInt32(values.Count);
                foreach (var value in values)
                {
                    WriteString(value);
                }
            }

            public void WriteUtcDateTimeOffset(DateTimeOffset value)
            {
                WriteInt64(value.ToUniversalTime().UtcDateTime.Ticks);
                WriteInt16(0);
            }

            public byte[] ToArray()
            {
                var payload = bytes.ToArray();
                if (payload.Length > MaxPayloadSize)
                {
                    throw new InvalidOperationException("Payload exceeds the maximum allowed size.");
                }

                return payload;
            }
        }

        private sealed class PayloadReader
        {
            private readonly ReadOnlyMemory<byte> payload;
            private int offset;

            public PayloadReader(ReadOnlyMemory<byte> payload)
            {
                this.payload = payload;
            }

            public bool HasRemaining
            {
                get { return offset < payload.Length; }
            }

            public byte ReadByte()
            {
                EnsureAvailable(sizeof(byte));
                return payload.Span[offset++];
            }

            public bool ReadBool()
            {
                var value = ReadByte();
                if (value == 0)
                {
                    return false;
                }

                if (value == 1)
                {
                    return true;
                }

                throw new InvalidOperationException("Boolean value must be 0 or 1.");
            }

            public short ReadInt16()
            {
                EnsureAvailable(sizeof(short));
                var value = BinaryPrimitives.ReadInt16BigEndian(payload.Span.Slice(offset, sizeof(short)));
                offset += sizeof(short);
                return value;
            }

            public int ReadInt32()
            {
                EnsureAvailable(sizeof(int));
                var value = BinaryPrimitives.ReadInt32BigEndian(payload.Span.Slice(offset, sizeof(int)));
                offset += sizeof(int);
                return value;
            }

            public long ReadInt64()
            {
                EnsureAvailable(sizeof(long));
                var value = BinaryPrimitives.ReadInt64BigEndian(payload.Span.Slice(offset, sizeof(long)));
                offset += sizeof(long);
                return value;
            }

            public string? ReadString()
            {
                var length = ReadInt32();
                if (length == -1)
                {
                    return null;
                }

                if (length < 0)
                {
                    throw new InvalidOperationException("String length cannot be negative.");
                }

                if (length > MaxPayloadSize)
                {
                    throw new InvalidOperationException("String exceeds the maximum allowed size.");
                }

                EnsureAvailable(length);
                string value;
                try
                {
                    value = StrictUtf8.GetString(payload.Span.Slice(offset, length));
                }
                catch (DecoderFallbackException ex)
                {
                    throw new InvalidOperationException("String payload contains malformed UTF-8.", ex);
                }

                offset += length;
                return value;
            }

            public string ReadStringAsEmptyIfNull()
            {
                return ReadString() ?? "";
            }

            public List<string> ReadStringListAsEmptyIfNull()
            {
                var count = ReadInt32();
                if (count == -1)
                {
                    return new List<string>();
                }

                if (count < 0)
                {
                    throw new InvalidOperationException("String list count cannot be negative.");
                }

                if (count > MaxStringListCount)
                {
                    throw new InvalidOperationException("String list exceeds the maximum allowed count.");
                }

                var values = new List<string>(count);
                for (var i = 0; i < count; i++)
                {
                    values.Add(ReadStringAsEmptyIfNull());
                }

                return values;
            }

            public DateTimeOffset ReadUtcDateTimeOffset()
            {
                var ticks = ReadInt64();
                var offsetMinutes = ReadInt16();
                if (offsetMinutes != 0)
                {
                    throw new InvalidOperationException("DateTimeOffset must use UTC offset.");
                }

                try
                {
                    return new DateTimeOffset(ticks, TimeSpan.Zero);
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    throw new InvalidOperationException("DateTimeOffset ticks are outside the valid range.", ex);
                }
            }

            public TEnum ReadEnum<TEnum>()
                where TEnum : struct, Enum
            {
                var value = (TEnum)Enum.ToObject(typeof(TEnum), ReadInt32());
                ValidateEnum(value, typeof(TEnum).Name);
                return value;
            }

            public void EnsureEnd()
            {
                if (offset != payload.Length)
                {
                    throw new InvalidOperationException("Payload contains trailing bytes.");
                }
            }

            private void EnsureAvailable(int count)
            {
                if (count < 0 || payload.Length - offset < count)
                {
                    throw new InvalidOperationException("Payload is truncated.");
                }
            }
        }
    }
}
