using System;
using System.Buffers.Binary;
using System.Text;

namespace Lakona.Rpc.Core
{
    /// <summary>
    /// Encodes and decodes Lakona.Rpc wire envelopes.
    /// </summary>
    /// <remarks>
    /// The codec serializes only the transport envelope fields. RPC method payloads are
    /// opaque bytes produced by an <see cref="IRpcSerializer"/>.
    /// </remarks>
    public static class RpcEnvelopeCodec
    {
        /// <summary>
        /// Maximum payload length accepted by envelope decoders.
        /// </summary>
        public const int MaxPayloadSize = RpcProtocolLimits.DefaultMaxPayloadSize;

        /// <summary>
        /// Reads the frame type byte from an encoded RPC envelope without decoding the full frame.
        /// </summary>
        /// <param name="data">Encoded envelope bytes.</param>
        /// <returns>The frame type stored in the first byte.</returns>
        /// <exception cref="InvalidOperationException">Thrown when <paramref name="data"/> is empty.</exception>
        public static RpcFrameType PeekFrameType(ReadOnlySpan<byte> data)
        {
            if (data.Length < 1)
                throw new InvalidOperationException("Frame is empty.");
            return (RpcFrameType)data[0];
        }

        /// <summary>
        /// Encodes a request envelope into a transport frame.
        /// </summary>
        /// <param name="req">Request metadata and serialized method payload.</param>
        /// <returns>An owned transport frame containing the encoded request envelope.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="req"/> is <see langword="null"/>.</exception>
        public static TransportFrame EncodeRequest(RpcRequestEnvelope req)
        {
            if (req is null) throw new ArgumentNullException(nameof(req));

            var payload = req.Payload;
            var total = 1 + 4 + 4 + 4 + 4 + payload.Length;
            var frame = TransportFrame.Allocate(total);
            var data = frame.GetWritableSpan();
            var offset = 0;

            data[offset++] = (byte)RpcFrameType.Request;
            WriteUInt32(data, ref offset, req.RequestId);
            WriteInt32(data, ref offset, req.ServiceId);
            WriteInt32(data, ref offset, req.MethodId);
            WriteInt32(data, ref offset, payload.Length);
            payload.Span.CopyTo(data.Slice(offset));
            return frame;
        }

        /// <summary>
        /// Decodes a request envelope from a transport frame.
        /// </summary>
        /// <param name="data">Encoded request frame.</param>
        /// <returns>A decoded request frame whose payload slice references <paramref name="data"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the frame type or envelope length is invalid.</exception>
        public static RpcRequestFrame DecodeRequest(TransportFrame data)
        {
            var offset = 0;
            var span = data.Span;
            var frameType = (RpcFrameType)ReadByte(span, ref offset);
            if (frameType != RpcFrameType.Request)
                throw new InvalidOperationException($"Expected Request frame, got {frameType}.");

            var requestId = ReadUInt32(span, ref offset);
            var serviceId = ReadInt32(span, ref offset);
            var methodId = ReadInt32(span, ref offset);
            var payloadLen = ReadInt32(span, ref offset);
            ValidateLength(payloadLen);
            EnsureRemaining(span, offset, payloadLen);

            var payload = data.Slice(offset, payloadLen);
            offset += payloadLen;
            if (offset != data.Length)
                throw new InvalidOperationException("Request envelope has extra trailing bytes.");

            return new RpcRequestFrame(requestId, serviceId, methodId, payload);
        }

        /// <summary>
        /// Encodes a response envelope into a transport frame.
        /// </summary>
        /// <param name="resp">Response metadata, status, serialized payload, and optional error message.</param>
        /// <returns>An owned transport frame containing the encoded response envelope.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="resp"/> is <see langword="null"/>.</exception>
        public static TransportFrame EncodeResponse(RpcResponseEnvelope resp)
        {
            if (resp is null) throw new ArgumentNullException(nameof(resp));
            return EncodeResponse(resp.RequestId, resp.Status, resp.Payload, resp.ErrorMessage);
        }

        /// <summary>
        /// Encodes response fields into a transport frame.
        /// </summary>
        /// <param name="requestId">Identifier of the request being answered.</param>
        /// <param name="status">Response status.</param>
        /// <param name="payload">Serialized return payload or empty bytes for non-success responses.</param>
        /// <param name="errorMessage">Optional UTF-8 error text included with the response.</param>
        /// <returns>An owned transport frame containing the encoded response envelope.</returns>
        public static TransportFrame EncodeResponse(
            uint requestId, RpcStatus status, ReadOnlyMemory<byte> payload, string? errorMessage = null)
        {
            var hasError = !string.IsNullOrEmpty(errorMessage);
            var errorBytes = hasError ? Encoding.UTF8.GetBytes(errorMessage!) : Array.Empty<byte>();

            var total = 1 + 4 + 1 + 4 + payload.Length + 1 + (hasError ? 4 + errorBytes.Length : 0);
            var frame = TransportFrame.Allocate(total);
            var data = frame.GetWritableSpan();
            var offset = 0;

            data[offset++] = (byte)RpcFrameType.Response;
            WriteUInt32(data, ref offset, requestId);
            data[offset++] = (byte)status;
            WriteInt32(data, ref offset, payload.Length);
            payload.Span.CopyTo(data.Slice(offset));
            offset += payload.Length;
            data[offset++] = hasError ? (byte)1 : (byte)0;

            if (hasError)
            {
                WriteInt32(data, ref offset, errorBytes.Length);
                errorBytes.AsSpan().CopyTo(data.Slice(offset));
            }

            return frame;
        }

        /// <summary>
        /// Decodes a response envelope from a transport frame.
        /// </summary>
        /// <param name="data">Encoded response frame.</param>
        /// <returns>A decoded response frame whose payload slice references <paramref name="data"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the frame type or envelope length is invalid.</exception>
        public static RpcResponseFrame DecodeResponse(TransportFrame data)
        {
            var offset = 0;
            var span = data.Span;
            var frameType = (RpcFrameType)ReadByte(span, ref offset);
            if (frameType != RpcFrameType.Response)
                throw new InvalidOperationException($"Expected Response frame, got {frameType}.");

            var requestId = ReadUInt32(span, ref offset);
            var status = (RpcStatus)ReadByte(span, ref offset);
            var payloadLen = ReadInt32(span, ref offset);
            ValidateLength(payloadLen);
            EnsureRemaining(span, offset, payloadLen);
            var payload = data.Slice(offset, payloadLen);
            offset += payloadLen;

            var hasError = ReadByte(span, ref offset) != 0;
            string? error = null;
            if (hasError)
            {
                var errLen = ReadInt32(span, ref offset);
                ValidateLength(errLen);
                EnsureRemaining(span, offset, errLen);
                error = Encoding.UTF8.GetString(span.Slice(offset, errLen));
                offset += errLen;
            }

            if (offset != data.Length)
                throw new InvalidOperationException("Response envelope has extra trailing bytes.");

            return new RpcResponseFrame(requestId, status, payload, error);
        }

        /// <summary>
        /// Encodes a server-to-client push envelope into a transport frame.
        /// </summary>
        /// <param name="push">Push metadata and serialized method payload.</param>
        /// <returns>An owned transport frame containing the encoded push envelope.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="push"/> is <see langword="null"/>.</exception>
        public static TransportFrame EncodePush(RpcPushEnvelope push)
        {
            if (push is null) throw new ArgumentNullException(nameof(push));

            var payload = push.Payload;
            var total = 1 + 4 + 4 + 4 + payload.Length;
            var frame = TransportFrame.Allocate(total);
            var data = frame.GetWritableSpan();
            var offset = 0;

            data[offset++] = (byte)RpcFrameType.Push;
            WriteInt32(data, ref offset, push.ServiceId);
            WriteInt32(data, ref offset, push.MethodId);
            WriteInt32(data, ref offset, payload.Length);
            payload.Span.CopyTo(data.Slice(offset));
            return frame;
        }

        /// <summary>
        /// Decodes a server-to-client push envelope from a transport frame.
        /// </summary>
        /// <param name="data">Encoded push frame.</param>
        /// <returns>A decoded push frame whose payload slice references <paramref name="data"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the frame type or envelope length is invalid.</exception>
        public static RpcPushFrame DecodePush(TransportFrame data)
        {
            var offset = 0;
            var span = data.Span;
            var frameType = (RpcFrameType)ReadByte(span, ref offset);
            if (frameType != RpcFrameType.Push)
                throw new InvalidOperationException($"Expected Push frame, got {frameType}.");

            var serviceId = ReadInt32(span, ref offset);
            var methodId = ReadInt32(span, ref offset);
            var payloadLen = ReadInt32(span, ref offset);
            ValidateLength(payloadLen);
            EnsureRemaining(span, offset, payloadLen);

            var payload = data.Slice(offset, payloadLen);
            offset += payloadLen;
            if (offset != data.Length)
                throw new InvalidOperationException("Push envelope has extra trailing bytes.");

            return new RpcPushFrame(serviceId, methodId, payload);
        }

        /// <summary>
        /// Encodes a keepalive ping envelope into a transport frame.
        /// </summary>
        /// <param name="ping">Ping timestamp data.</param>
        /// <returns>An owned transport frame containing the encoded keepalive ping.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="ping"/> is <see langword="null"/>.</exception>
        public static TransportFrame EncodeKeepAlivePing(RpcKeepAlivePingEnvelope ping)
        {
            if (ping is null) throw new ArgumentNullException(nameof(ping));

            var frame = TransportFrame.Allocate(1 + 8);
            var data = frame.GetWritableSpan();
            var offset = 0;
            data[offset++] = (byte)RpcFrameType.KeepAlivePing;
            WriteInt64(data, ref offset, ping.TimestampTicksUtc);
            return frame;
        }

        /// <summary>
        /// Decodes a keepalive ping envelope.
        /// </summary>
        /// <param name="data">Encoded keepalive ping bytes.</param>
        /// <returns>The decoded keepalive ping envelope.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the frame type or envelope length is invalid.</exception>
        public static RpcKeepAlivePingEnvelope DecodeKeepAlivePing(ReadOnlySpan<byte> data)
        {
            var offset = 0;
            var frameType = (RpcFrameType)ReadByte(data, ref offset);
            if (frameType != RpcFrameType.KeepAlivePing)
                throw new InvalidOperationException($"Expected KeepAlivePing frame, got {frameType}.");

            var timestampTicksUtc = ReadInt64(data, ref offset);
            if (offset != data.Length)
                throw new InvalidOperationException("KeepAlivePing envelope has extra trailing bytes.");

            return new RpcKeepAlivePingEnvelope
            {
                TimestampTicksUtc = timestampTicksUtc
            };
        }

        /// <summary>
        /// Encodes a keepalive pong envelope into a transport frame.
        /// </summary>
        /// <param name="pong">Pong timestamp data.</param>
        /// <returns>An owned transport frame containing the encoded keepalive pong.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="pong"/> is <see langword="null"/>.</exception>
        public static TransportFrame EncodeKeepAlivePong(RpcKeepAlivePongEnvelope pong)
        {
            if (pong is null) throw new ArgumentNullException(nameof(pong));

            var frame = TransportFrame.Allocate(1 + 8);
            var data = frame.GetWritableSpan();
            var offset = 0;
            data[offset++] = (byte)RpcFrameType.KeepAlivePong;
            WriteInt64(data, ref offset, pong.TimestampTicksUtc);
            return frame;
        }

        /// <summary>
        /// Decodes a keepalive pong envelope.
        /// </summary>
        /// <param name="data">Encoded keepalive pong bytes.</param>
        /// <returns>The decoded keepalive pong envelope.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the frame type or envelope length is invalid.</exception>
        public static RpcKeepAlivePongEnvelope DecodeKeepAlivePong(ReadOnlySpan<byte> data)
        {
            var offset = 0;
            var frameType = (RpcFrameType)ReadByte(data, ref offset);
            if (frameType != RpcFrameType.KeepAlivePong)
                throw new InvalidOperationException($"Expected KeepAlivePong frame, got {frameType}.");

            var timestampTicksUtc = ReadInt64(data, ref offset);
            if (offset != data.Length)
                throw new InvalidOperationException("KeepAlivePong envelope has extra trailing bytes.");

            return new RpcKeepAlivePongEnvelope
            {
                TimestampTicksUtc = timestampTicksUtc
            };
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> data, ref int offset)
        {
            EnsureRemaining(data, offset, 4);
            var value = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
            offset += 4;
            return value;
        }

        private static int ReadInt32(ReadOnlySpan<byte> data, ref int offset)
        {
            EnsureRemaining(data, offset, 4);
            var value = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4));
            offset += 4;
            return value;
        }

        private static long ReadInt64(ReadOnlySpan<byte> data, ref int offset)
        {
            EnsureRemaining(data, offset, 8);
            var value = BinaryPrimitives.ReadInt64BigEndian(data.Slice(offset, 8));
            offset += 8;
            return value;
        }

        private static byte ReadByte(ReadOnlySpan<byte> data, ref int offset)
        {
            EnsureRemaining(data, offset, 1);
            return data[offset++];
        }

        private static void WriteUInt32(Span<byte> data, ref int offset, uint value)
        {
            BinaryPrimitives.WriteUInt32BigEndian(data.Slice(offset, 4), value);
            offset += 4;
        }

        private static void WriteInt32(Span<byte> data, ref int offset, int value)
        {
            BinaryPrimitives.WriteInt32BigEndian(data.Slice(offset, 4), value);
            offset += 4;
        }

        private static void WriteInt64(Span<byte> data, ref int offset, long value)
        {
            BinaryPrimitives.WriteInt64BigEndian(data.Slice(offset, 8), value);
            offset += 8;
        }

        private static void EnsureRemaining(ReadOnlySpan<byte> data, int offset, int count)
        {
            if (offset < 0 || count < 0 || data.Length - offset < count)
                throw new InvalidOperationException("RPC envelope is malformed.");
        }

        private static void ValidateLength(int length)
        {
            if (length < 0 || length > MaxPayloadSize)
                throw new InvalidOperationException($"RPC envelope length is invalid: {length}");
        }
    }
}
